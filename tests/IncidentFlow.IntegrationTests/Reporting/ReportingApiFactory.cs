using System.Text.Json;
using IncidentFlow.IntegrationTests.Shared;
using IncidentFlow.Services.Reporting.Api.Data;
using IncidentFlow.Services.Reporting.Api.Rebuild;
using IncidentFlow.Services.Timeline.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace IncidentFlow.IntegrationTests.Reporting;

// Both APIs keep their top-level Program internal; any public type from the entry assembly works as
// WebApplicationFactory's TEntryPoint, so no extern alias is needed.

/// <summary>The Reporting API on the given database; its bus is built but never started.</summary>
public sealed class ReportingApiFactory(string connectionString, HttpMessageHandler? timeline = null, int? rebuildPageSize = null)
    : WebApplicationFactory<ReportingDbContext>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:reporting-db", connectionString);
        builder.UseSetting("ConnectionStrings:rabbitmq", "amqp://guest:guest@localhost:5672");
        // What Aspire injects for WithReference(timeline); the handler below answers in-process.
        builder.UseSetting("services:timeline:http:0", "http://localhost");
        if (rebuildPageSize is { } size)
            builder.UseSetting("Reporting:Rebuild:PageSize", size.ToString());

        builder.ConfigureServices(services =>
        {
            services.RemoveMassTransitHostedServices();
            if (timeline is not null)
                services.AddHttpClient(TimelineClient.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => timeline);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        NpgsqlConnection.ClearAllPools(); // see ConsumerHost.DisposeAsync
    }
}

/// <summary>The Timeline API on the given database; its bus is built but never started.</summary>
public sealed class TimelineApiFactory(string connectionString) : WebApplicationFactory<TimelineDbContext>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:timeline-db", connectionString);
        builder.UseSetting("ConnectionStrings:rabbitmq", "amqp://guest:guest@localhost:5672");
        builder.ConfigureServices(services => services.RemoveMassTransitHostedServices());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        NpgsqlConnection.ClearAllPools(); // see ConsumerHost.DisposeAsync
    }
}

// The JSON the clients read, declared here so a renamed property fails the tests.
public sealed record SummaryDto(
    int TotalIncidents,
    int OpenIncidents,
    int ResolvedIncidents,
    double MeanTimeToAcknowledgeMinutes,
    double MeanTimeToResolveMinutes,
    Dictionary<string, int> SeverityBreakdown,
    Dictionary<string, int> TopFailingServices,
    Dictionary<string, int> DeploymentRegressionStats);

public sealed record MinutesDto(double Minutes);

public sealed record ReliabilityRowDto(
    string ServiceName,
    string? Environment,
    int Incidents,
    double MeanTimeToAcknowledgeMinutes,
    double MeanTimeToResolveMinutes);

public sealed record TimelinePageDto(List<TimelineEventDto> Items, long NextCursor);

public sealed record TimelineEventDto(
    long Sequence,
    string EventType,
    Guid? IncidentId,
    JsonElement? Payload,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt);
