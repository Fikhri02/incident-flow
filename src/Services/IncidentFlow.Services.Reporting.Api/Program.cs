using IncidentFlow.BuildingBlocks.Messaging;
using IncidentFlow.ServiceDefaults;
using IncidentFlow.Services.Reporting.Api.Consumers;
using IncidentFlow.Services.Reporting.Api.Data;
using IncidentFlow.Services.Reporting.Api.Endpoints;
using IncidentFlow.Services.Reporting.Api.Rebuild;
using IncidentFlow.Services.Reporting.Api.Reports;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<ReportingDbContext>("reporting-db");

builder.Services.AddScoped<ReportQueries>();
builder.Services.AddScoped<ReportRebuilder>();
builder.Services.Configure<RebuildOptions>(builder.Configuration.GetSection("Reporting:Rebuild"));
builder.Services.AddHttpClient<TimelineClient>(TimelineClient.HttpClientName, http => http.BaseAddress = TimelineClient.BaseAddress);

builder.Services.AddMassTransit(x =>
{
    x.AddReportingMessaging();

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("rabbitmq"));
        cfg.UseDefaultRetry();
        cfg.UsePayloadCorrelationId();
        cfg.ConfigureEndpoints(ctx);
    });
});

var app = builder.Build();

app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    var (status, error) = ctx.Features.Get<IExceptionHandlerFeature>()?.Error switch
    {
        BadHttpRequestException => (400, "Invalid request."),
        HttpRequestException => (502, "Timeline could not be read; the rebuild stopped and kept the pages it had applied."),
        _ => (500, "An unexpected error occurred.")
    };
    ctx.Response.StatusCode = status;
    await ctx.Response.WriteAsJsonAsync(new { error });
}));

app.MapDefaultEndpoints();
app.MapReportEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
