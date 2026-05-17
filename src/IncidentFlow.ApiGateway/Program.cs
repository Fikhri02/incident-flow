using System.Text.Json.Serialization;
using IncidentFlow.Core;
using IncidentFlow.ServiceDefaults;
using Microsoft.AspNetCore.Diagnostics;
using Yarp.ReverseProxy.Configuration;

const string CorrelationIdHeader = "x-correlation-id";
const string ApiKeyHeader = "x-api-key";

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddSingleton<IncidentFlowEngine>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.WriteIndented = true;
});

// YARP — proxy signal + incident routes to their respective services
// Aspire injects: services__<name>__https__0 / services__<name>__http__0
var ingestionBase =
    builder.Configuration["services:ingestion:https:0"] ??
    builder.Configuration["services:ingestion:http:0"] ??
    "http://ingestion";

var incidentBase =
    builder.Configuration["services:incident:https:0"] ??
    builder.Configuration["services:incident:http:0"] ??
    "http://incident";

var yarpRoutes = new[]
{
    new RouteConfig
    {
        RouteId = "signals",
        ClusterId = "ingestion",
        Match = new RouteMatch { Path = "/api/v1/signals/{**remainder}" }
    },
    new RouteConfig
    {
        RouteId = "incidents",
        ClusterId = "incident",
        Match = new RouteMatch { Path = "/api/v1/incidents/{**remainder}" }
    }
};

var yarpClusters = new[]
{
    new ClusterConfig
    {
        ClusterId = "ingestion",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["ingestion/0"] = new DestinationConfig { Address = ingestionBase }
        }
    },
    new ClusterConfig
    {
        ClusterId = "incident",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["incident/0"] = new DestinationConfig { Address = incidentBase }
        }
    }
};

builder.Services.AddReverseProxy()
    .LoadFromMemory(yarpRoutes, yarpClusters);

var app = builder.Build();

app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, message) = ex switch
    {
        ArgumentException => (400, ex.Message),
        InvalidOperationException when ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) => (429, ex.Message),
        InvalidOperationException => (409, ex.Message),
        KeyNotFoundException => (404, ex.Message),
        _ => (500, ex?.Message ?? "An unexpected error occurred.")
    };
    ctx.Response.StatusCode = status;
    await ctx.Response.WriteAsJsonAsync(new { error = message });
}));

// Correlation ID
app.Use(async (ctx, next) =>
{
    var id = ctx.Request.Headers[CorrelationIdHeader].FirstOrDefault() ?? Guid.NewGuid().ToString("N");
    ctx.Items[CorrelationIdHeader] = id;
    ctx.Response.Headers[CorrelationIdHeader] = id;
    await next();
});

// API key guard
app.Use(async (ctx, next) =>
{
    var configuredKey = Environment.GetEnvironmentVariable("INCIDENTFLOW_API_KEY");
    var path = ctx.Request.Path.Value ?? "/";
    var exempt = string.IsNullOrWhiteSpace(configuredKey)
                 || path == "/"
                 || path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase);
    if (!exempt && ctx.Request.Headers[ApiKeyHeader] != configuredKey)
    {
        ctx.Response.StatusCode = 401;
        await ctx.Response.WriteAsJsonAsync(new { error = "API key is required." });
        return;
    }
    await next();
});

app.MapDefaultEndpoints();
app.MapReverseProxy();

static string CorrelationId(HttpContext ctx) =>
    ctx.Items[CorrelationIdHeader] as string ?? Guid.NewGuid().ToString("N");

static ReportFilter ReportFilter(HttpContext ctx) => new(
    ctx.Request.Query["serviceName"].FirstOrDefault(),
    ctx.Request.Query["environment"].FirstOrDefault()
);

// Root
app.MapGet("/", () => new
{
    service = "IncidentFlow",
    status = "running",
    workflow = "signals -> detection -> correlation -> incident -> notification -> timeline -> recommendation -> resolution -> postmortem -> reporting"
});

// Events
app.MapGet("/api/v1/events", (IncidentFlowEngine e) => e.GetEvents());
app.MapGet("/api/v1/events/outbox", (IncidentFlowEngine e) => e.GetOutboxMessages());
app.MapGet("/api/v1/events/inbox", (IncidentFlowEngine e) => e.GetInboxMessages());

// Notifications
app.MapPost("/api/v1/notifications/retry-failed", (HttpContext ctx, IncidentFlowEngine e) =>
    Results.Ok(e.RetryFailedNotifications(CorrelationId(ctx))));

// Reports
app.MapGet("/api/v1/reports/summary", (HttpContext ctx, IncidentFlowEngine e) =>
    Results.Ok(e.GetReport(ReportFilter(ctx))));

app.MapPost("/api/v1/reports/rebuild", (HttpContext ctx, IncidentFlowEngine e) =>
    Results.Ok(e.RebuildReport(ReportFilter(ctx), CorrelationId(ctx))));

app.MapGet("/api/v1/reports/service-reliability", (HttpContext ctx, IncidentFlowEngine e) =>
    Results.Ok(e.GetReport(ReportFilter(ctx)).TopFailingServices));

app.MapGet("/api/v1/reports/mtta", (HttpContext ctx, IncidentFlowEngine e) =>
    Results.Ok(new { minutes = e.GetReport(ReportFilter(ctx)).MeanTimeToAcknowledgeMinutes }));

app.MapGet("/api/v1/reports/mttr", (HttpContext ctx, IncidentFlowEngine e) =>
    Results.Ok(new { minutes = e.GetReport(ReportFilter(ctx)).MeanTimeToResolveMinutes }));

app.MapGet("/api/v1/reports/severity-breakdown", (HttpContext ctx, IncidentFlowEngine e) =>
    Results.Ok(e.GetReport(ReportFilter(ctx)).SeverityBreakdown));

app.MapGet("/api/v1/reports/top-failing-services", (HttpContext ctx, IncidentFlowEngine e) =>
    Results.Ok(e.GetReport(ReportFilter(ctx)).TopFailingServices));

app.MapGet("/api/v1/reports/deployment-regressions", (HttpContext ctx, IncidentFlowEngine e) =>
    Results.Ok(e.GetReport(ReportFilter(ctx)).DeploymentRegressionStats));

// Runtime
app.MapGet("/api/v1/runtime/readiness", (IncidentFlowEngine e) => e.GetRuntimeReadiness());

app.Run();
