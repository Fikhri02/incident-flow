using IncidentFlow.Services.Reporting.Api.Rebuild;
using IncidentFlow.Services.Reporting.Api.Reports;

namespace IncidentFlow.Services.Reporting.Api.Endpoints;

/// <summary>The prototype's /api/v1/reports routes, same paths and JSON, each with serviceName and environment filters.</summary>
public static class ReportEndpoints
{
    public static void MapReportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/reports").WithTags("reports");

        group.MapGet("/summary", async (string? serviceName, string? environment, ReportQueries reports, CancellationToken ct) =>
            Results.Ok(await reports.SummaryAsync(new ReportFilter(serviceName, environment), ct)));

        group.MapGet("/mtta", async (string? serviceName, string? environment, ReportQueries reports, CancellationToken ct) =>
            Results.Ok(new { minutes = (await reports.SummaryAsync(new ReportFilter(serviceName, environment), ct)).MeanTimeToAcknowledgeMinutes }));

        group.MapGet("/mttr", async (string? serviceName, string? environment, ReportQueries reports, CancellationToken ct) =>
            Results.Ok(new { minutes = (await reports.SummaryAsync(new ReportFilter(serviceName, environment), ct)).MeanTimeToResolveMinutes }));

        group.MapGet("/severity-breakdown", async (string? serviceName, string? environment, ReportQueries reports, CancellationToken ct) =>
            Results.Ok((await reports.SummaryAsync(new ReportFilter(serviceName, environment), ct)).SeverityBreakdown));

        group.MapGet("/top-failing-services", async (string? serviceName, string? environment, ReportQueries reports, CancellationToken ct) =>
            Results.Ok((await reports.SummaryAsync(new ReportFilter(serviceName, environment), ct)).TopFailingServices));

        group.MapGet("/deployment-regressions", async (string? serviceName, string? environment, ReportQueries reports, CancellationToken ct) =>
            Results.Ok((await reports.SummaryAsync(new ReportFilter(serviceName, environment), ct)).DeploymentRegressionStats));

        group.MapGet("/service-reliability", async (string? serviceName, string? environment, ReportQueries reports, CancellationToken ct) =>
            Results.Ok(await reports.ServiceReliabilityAsync(new ReportFilter(serviceName, environment), ct)));

        // Replays every incident from Timeline (clean=true empties the read model first); the filters only shape
        // the summary it returns. The rebuild runs on the host's shutdown token, not the request's: a client
        // that gives up does not stop it half way. Each page commits on its own, so even a stop leaves a
        // consistent read model.
        group.MapPost("/rebuild", async (string? serviceName, string? environment, bool? clean,
            ReportRebuilder rebuilder, ReportQueries reports, IHostApplicationLifetime lifetime, CancellationToken ct) =>
        {
            if (await rebuilder.TryRebuildAsync(clean ?? false, lifetime.ApplicationStopping) is null)
                return Results.Conflict(new { error = "A rebuild is already running." });
            return Results.Ok(await reports.SummaryAsync(new ReportFilter(serviceName, environment), ct));
        });
    }
}
