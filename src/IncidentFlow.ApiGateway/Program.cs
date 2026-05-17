using System.Text.Json.Serialization;
using IncidentFlow.Core;
using IncidentFlow.ServiceDefaults;
using Microsoft.AspNetCore.Diagnostics;

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

// Signals
app.MapPost("/api/v1/signals/errors", (ErrorSignalRequest req, HttpContext ctx, IncidentFlowEngine e) =>
    Results.Json(e.IngestError(req, CorrelationId(ctx)), statusCode: 202));

app.MapPost("/api/v1/signals/logs", (LogSignalRequest req, HttpContext ctx, IncidentFlowEngine e) =>
    Results.Json(e.IngestLog(req, CorrelationId(ctx)), statusCode: 202));

app.MapPost("/api/v1/signals/metrics", (MetricSignalRequest req, HttpContext ctx, IncidentFlowEngine e) =>
    Results.Json(e.IngestMetric(req, CorrelationId(ctx)), statusCode: 202));

app.MapPost("/api/v1/signals/traces", (TraceSignalRequest req, HttpContext ctx, IncidentFlowEngine e) =>
    Results.Json(e.IngestTrace(req, CorrelationId(ctx)), statusCode: 202));

app.MapPost("/api/v1/signals/health-checks", (HealthCheckSignalRequest req, HttpContext ctx, IncidentFlowEngine e) =>
    Results.Json(e.IngestHealthCheck(req, CorrelationId(ctx)), statusCode: 202));

app.MapPost("/api/v1/signals/deployments", (DeploymentSignalRequest req, HttpContext ctx, IncidentFlowEngine e) =>
    Results.Json(e.IngestDeployment(req, CorrelationId(ctx)), statusCode: 202));

app.MapPost("/api/v1/signals/user-reports", (UserReportSignalRequest req, HttpContext ctx, IncidentFlowEngine e) =>
    Results.Json(e.IngestUserReport(req, CorrelationId(ctx)), statusCode: 202));

app.MapGet("/api/v1/signals", (IncidentFlowEngine e) => e.GetSignals());

// Incidents
app.MapGet("/api/v1/incidents", (IncidentFlowEngine e) => e.GetIncidents());
app.MapGet("/api/v1/incidents/open", (IncidentFlowEngine e) => e.GetOpenIncidents());

app.MapGet("/api/v1/incidents/{id:guid}", (Guid id, IncidentFlowEngine e) =>
{
    var incident = e.GetIncident(id);
    return incident is null ? Results.NotFound(new { error = "Not found." }) : Results.Ok(incident);
});

app.MapGet("/api/v1/incidents/{id:guid}/timeline", (Guid id, IncidentFlowEngine e) => e.GetTimeline(id));
app.MapGet("/api/v1/incidents/{id:guid}/notifications", (Guid id, IncidentFlowEngine e) => e.GetNotifications(id));
app.MapGet("/api/v1/incidents/{id:guid}/recommendations", (Guid id, IncidentFlowEngine e) => e.GetRecommendations(id));

app.MapGet("/api/v1/incidents/{id:guid}/postmortem", (Guid id, IncidentFlowEngine e) =>
{
    var pm = e.GetPostmortemForIncident(id);
    return pm is null ? Results.NotFound(new { error = "Not found." }) : Results.Ok(pm);
});

app.MapPost("/api/v1/incidents/{id:guid}/acknowledge", (Guid id, AcknowledgeIncidentRequest req, HttpContext ctx, IncidentFlowEngine e) =>
    Results.Ok(e.AcknowledgeIncident(id, req, CorrelationId(ctx))));

app.MapPost("/api/v1/incidents/{id:guid}/assign", (Guid id, AssignIncidentRequest req, HttpContext ctx, IncidentFlowEngine e) =>
    Results.Ok(e.AssignIncident(id, req, CorrelationId(ctx))));

app.MapPost("/api/v1/incidents/{id:guid}/escalate", (Guid id, EscalateIncidentRequest req, HttpContext ctx, IncidentFlowEngine e) =>
    Results.Ok(e.EscalateIncident(id, req, CorrelationId(ctx))));

app.MapPost("/api/v1/incidents/{id:guid}/resolve", (Guid id, ResolveIncidentRequest req, HttpContext ctx, IncidentFlowEngine e) =>
    Results.Ok(e.ResolveIncident(id, req, CorrelationId(ctx))));

app.MapPost("/api/v1/incidents/{id:guid}/close", (Guid id, HttpContext ctx, IncidentFlowEngine e) =>
    Results.Ok(e.CloseIncident(id, CorrelationId(ctx))));

app.MapPost("/api/v1/incidents/{id:guid}/reopen", (Guid id, ReopenIncidentRequest req, HttpContext ctx, IncidentFlowEngine e) =>
    Results.Ok(e.ReopenIncident(id, req, CorrelationId(ctx))));

app.MapPost("/api/v1/incidents/{id:guid}/postmortem", (Guid id, HttpContext ctx, IncidentFlowEngine e) =>
    Results.Ok(e.GeneratePostmortem(id, CorrelationId(ctx))));

app.MapPut("/api/v1/incidents/{id:guid}/postmortem", (Guid id, UpdatePostmortemRequest req, HttpContext ctx, IncidentFlowEngine e) =>
    Results.Ok(e.UpdatePostmortem(id, req, CorrelationId(ctx))));

app.MapPost("/api/v1/incidents/{id:guid}/recommendations/{recId:guid}/status",
    (Guid id, Guid recId, UpdateRecommendationStatusRequest req, HttpContext ctx, IncidentFlowEngine e) =>
        Results.Ok(e.UpdateRecommendationStatus(id, recId, req, CorrelationId(ctx))));

app.MapPost("/api/v1/incidents/escalation-sweep", async (HttpContext ctx, IncidentFlowEngine e) =>
{
    EscalationSweepRequest req = new();
    if (ctx.Request.ContentLength > 0)
        req = await ctx.Request.ReadFromJsonAsync<EscalationSweepRequest>() ?? req;
    return Results.Ok(e.RunEscalationSweep(req, CorrelationId(ctx)));
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
