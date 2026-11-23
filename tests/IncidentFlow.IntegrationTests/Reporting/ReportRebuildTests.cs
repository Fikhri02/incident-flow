using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using IncidentFlow.IntegrationTests.Shared;
using IncidentFlow.Services.Reporting.Api.Consumers;
using IncidentFlow.Services.Reporting.Api.Data;
using IncidentFlow.Services.Timeline.Api.Consumers;
using IncidentFlow.Services.Timeline.Api.Data;
using Xunit;
using static IncidentFlow.IntegrationTests.Reporting.KnownIncidents;

namespace IncidentFlow.IntegrationTests.Reporting;

// Rebuild replays what Timeline stored, page by page, through the same projection the consumers use, and
// commits each page; ?clean=true empties the read model first. Both hosts are real: Timeline's consumers fill
// its database, the Timeline API serves the pages in-process, and the Reporting API calls it through its
// "timeline" HttpClient.
public sealed class ReportRebuildTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private sealed record Hosts(
        ConsumerHost<TimelineDbContext> Timeline,
        TimelineApiFactory TimelineApi,
        ConsumerHost<ReportingDbContext> Reporting,
        ReportingApiFactory ReportingApi) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await ReportingApi.DisposeAsync();
            await TimelineApi.DisposeAsync();
            await Reporting.DisposeAsync();
            await Timeline.DisposeAsync();
        }
    }

    /// <summary>The same events go to Timeline and to Reporting's live projection, as the broker would.</summary>
    private async Task<Hosts> StartAsync(object[] events, int pageSize,
        Func<HttpMessageHandler, HttpMessageHandler>? wrapTimeline = null)
    {
        var timelineDb = postgres.NewDatabase();
        var reportingDb = postgres.NewDatabase();
        var timeline = await ConsumerHost<TimelineDbContext>.StartAsync(timelineDb, x => x.AddTimelineMessaging());
        var reporting = await ConsumerHost<ReportingDbContext>.StartAsync(reportingDb, x => x.AddReportingMessaging());
        await timeline.PublishAsync(events);
        await reporting.PublishAsync(events);

        var timelineApi = new TimelineApiFactory(timelineDb);
        var handler = timelineApi.Server.CreateHandler();
        var reportingApi = new ReportingApiFactory(reportingDb, wrapTimeline?.Invoke(handler) ?? handler, pageSize);
        return new Hosts(timeline, timelineApi, reporting, reportingApi);
    }

    [Fact]
    public async Task Rebuild_FromTimeline_EqualsTheLiveProjection()
    {
        await using var hosts = await StartAsync(Events(), pageSize: 3);
        var client = hosts.ReportingApi.CreateClient();
        var live = await client.GetFromJsonAsync<SummaryDto>("/api/v1/reports/summary");
        var liveReliability = await client.GetFromJsonAsync<List<ReliabilityRowDto>>("/api/v1/reports/service-reliability");

        var response = await client.PostAsync("/api/v1/reports/rebuild", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rebuilt = await response.Content.ReadFromJsonAsync<SummaryDto>();
        rebuilt.Should().BeEquivalentTo(live);
        rebuilt!.TotalIncidents.Should().Be(5, "the live projection saw all five incidents");
        (await client.GetFromJsonAsync<SummaryDto>("/api/v1/reports/summary")).Should().BeEquivalentTo(live);
        (await client.GetFromJsonAsync<List<ReliabilityRowDto>>("/api/v1/reports/service-reliability"))
            .Should().Equal(liveReliability!);
    }

    [Fact]
    public async Task Rebuild_KeepsRowsTimelineHasNotStoredYet()
    {
        await using var hosts = await StartAsync(Events(), pageSize: 500);
        var client = hosts.ReportingApi.CreateClient();

        // An incident Reporting has consumed but Timeline has not stored yet (its consumer is behind).
        await hosts.Reporting.PublishAsync(Created(Guid.NewGuid(), "ghost-api", "prod", "Low"));

        var response = await client.PostAsync("/api/v1/reports/rebuild", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rebuilt = await response.Content.ReadFromJsonAsync<SummaryDto>();
        rebuilt!.TotalIncidents.Should().Be(6, "the default rebuild replays over the read model and deletes nothing");
        rebuilt.TopFailingServices.Should().ContainKey("ghost-api");
    }

    [Fact]
    public async Task CleanRebuild_ClearsRowsTimelineNeverRecorded()
    {
        await using var hosts = await StartAsync(Events(), pageSize: 3);
        var client = hosts.ReportingApi.CreateClient();
        var live = await client.GetFromJsonAsync<SummaryDto>("/api/v1/reports/summary");

        // Drift: an incident only the read model knows about.
        await hosts.Reporting.PublishAsync(Created(Guid.NewGuid(), "ghost-api", "prod", "Low"));
        (await client.GetFromJsonAsync<SummaryDto>("/api/v1/reports/summary"))!.TotalIncidents.Should().Be(6);

        var response = await client.PostAsync("/api/v1/reports/rebuild?clean=true", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<SummaryDto>()).Should().BeEquivalentTo(live);
    }

    [Fact]
    public async Task SecondRebuild_WhileOneIsRunning_IsRefusedWith409()
    {
        PauseOnSecondPage? pause = null;
        await using var hosts = await StartAsync(Events(), pageSize: 3, h => pause = new PauseOnSecondPage(h));
        var client = hosts.ReportingApi.CreateClient();

        var first = client.PostAsync("/api/v1/reports/rebuild", null);
        try
        {
            await pause!.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var second = await client.PostAsync("/api/v1/reports/rebuild?clean=true", null, timeout.Token);

            second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            pause!.Release.TrySetResult();
        }

        (await first).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync("/api/v1/reports/rebuild", null)).StatusCode.Should().Be(HttpStatusCode.OK,
            "the guard is released when a rebuild finishes");
    }

    [Fact]
    public async Task LiveEvent_ConsumedWhileARebuildIsRunning_IsAppliedWithoutWaiting_AndKept()
    {
        PauseOnSecondPage? pause = null;
        await using var hosts = await StartAsync(Events(), pageSize: 3, h => pause = new PauseOnSecondPage(h));
        var client = hosts.ReportingApi.CreateClient();

        var rebuild = client.PostAsync("/api/v1/reports/rebuild", null);
        try
        {
            await pause!.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));

            // Timeline is held mid-rebuild; the consumer must not wait behind the rebuild's writes. (Held in
            // one long transaction, it waited until the timeline client's 10-second attempt timeout freed it.)
            await hosts.Reporting.PublishAsync(Resolved(C, "payment-api", "staging", 45)).WaitAsync(TimeSpan.FromSeconds(5));

            var during = await client.GetFromJsonAsync<SummaryDto>("/api/v1/reports/summary");
            during!.ResolvedIncidents.Should().Be(3, "C's resolution was applied while the rebuild was paused");
        }
        finally
        {
            pause!.Release.TrySetResult();
        }

        var response = await rebuild;
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await response.Content.ReadFromJsonAsync<SummaryDto>();
        after!.ResolvedIncidents.Should().Be(3, "the rebuild does not undo an event Timeline has not stored yet");
        after.OpenIncidents.Should().Be(1);
    }

    [Fact]
    public async Task Rebuild_AppliesAnIncidentCreatedWhoseTextFieldsAreNull()
    {
        var database = postgres.NewDatabase();
        var id = Guid.NewGuid();
        // Nothing stops a publisher sending nulls the C# contract calls non-nullable; JSON carries them.
        var payload = new
        {
            incidentId = id, serviceName = (string?)null, environment = (string?)null, region = (string?)null,
            fingerprint = (string?)null, deploymentId = (string?)null, severity = (string?)null,
            ruleName = "ErrorSpike", summary = "unknown failing", detectedAt = T0, correlationId = "c"
        };
        var page = new
        {
            items = new[]
            {
                new { sequence = 1L, eventType = "incident.created", incidentId = (Guid?)id, payload, occurredAt = T0, recordedAt = T0 }
            },
            nextCursor = 1L
        };
        using var api = new ReportingApiFactory(database, new StubTimeline(page), rebuildPageSize: 500);

        var response = await api.CreateClient().PostAsync("/api/v1/reports/rebuild", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rebuilt = await response.Content.ReadFromJsonAsync<SummaryDto>();
        rebuilt!.TotalIncidents.Should().Be(1);
        rebuilt.SeverityBreakdown.Should().BeEquivalentTo(new Dictionary<string, int> { [""] = 1 });
    }

    [Fact]
    public async Task Rebuild_ReturnsTheFilteredSummary_WhenFiltersAreGiven()
    {
        await using var hosts = await StartAsync(Events(), pageSize: 4);
        var client = hosts.ReportingApi.CreateClient();
        var live = await client.GetFromJsonAsync<SummaryDto>("/api/v1/reports/summary?environment=staging");

        var rebuilt = await (await client.PostAsync("/api/v1/reports/rebuild?environment=staging", null))
            .Content.ReadFromJsonAsync<SummaryDto>();

        rebuilt.Should().BeEquivalentTo(live);
        rebuilt!.TotalIncidents.Should().Be(1);
        (await client.GetFromJsonAsync<SummaryDto>("/api/v1/reports/summary"))!.TotalIncidents.Should().Be(5,
            "the filter shapes the response; the rebuild itself covers every incident");
    }

    /// <summary>Lets Timeline's first page through and holds the next request until released.</summary>
    private sealed class PauseOnSecondPage(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private int _paused;

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!request.RequestUri!.Query.Contains("after=0&") && Interlocked.Exchange(ref _paused, 1) == 0)
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }

            return await base.SendAsync(request, ct);
        }
    }

    /// <summary>A Timeline that serves one fixed page from the start and an empty page after it.</summary>
    private sealed class StubTimeline(object firstPage) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.RequestUri!.Query.Contains("after=0&")
                ? JsonSerializer.Serialize(firstPage, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                : """{"items":[],"nextCursor":1}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
