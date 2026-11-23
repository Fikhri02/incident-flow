using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IncidentFlow.IntegrationTests.Shared;
using IncidentFlow.Services.Reporting.Api.Consumers;
using IncidentFlow.Services.Reporting.Api.Data;
using Xunit;
using static IncidentFlow.IntegrationTests.Reporting.KnownIncidents;

namespace IncidentFlow.IntegrationTests.Reporting;

// Every figure the reports return, computed from the lifecycle events in KnownIncidents and
// read over HTTP from the Reporting API, as the gateway's clients see it.
public sealed class ReportFiguresTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private async Task<(ConsumerHost<ReportingDbContext> Host, ReportingApiFactory Api)> ProjectAsync(params object[] events)
    {
        var database = postgres.NewDatabase();
        var host = await ConsumerHost<ReportingDbContext>.StartAsync(database, x => x.AddReportingMessaging());
        await host.PublishAsync(events);
        return (host, new ReportingApiFactory(database));
    }

    [Fact]
    public async Task Summary_FromKnownEvents_HasEveryFigure()
    {
        var (host, api) = await ProjectAsync(Events());
        await using var _ = host;
        using var __ = api;

        var summary = await api.CreateClient().GetFromJsonAsync<SummaryDto>("/api/v1/reports/summary");

        summary!.TotalIncidents.Should().Be(5);
        summary.OpenIncidents.Should().Be(2, "C and D are neither resolved nor closed");
        summary.ResolvedIncidents.Should().Be(2, "A and B have a resolved time; E was closed without one");
        summary.MeanTimeToAcknowledgeMinutes.Should().BeApproximately(20, 1e-9, "mean of 10, 20, 30");
        summary.MeanTimeToResolveMinutes.Should().BeApproximately(90, 1e-9, "mean of 60 and 120");
        summary.SeverityBreakdown.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["Critical"] = 2, ["High"] = 2, ["Low"] = 1
        });
        summary.TopFailingServices.Keys.Should().Equal("payment-api", "auth-api", "search-api");
        summary.TopFailingServices.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["payment-api"] = 3, ["auth-api"] = 1, ["search-api"] = 1
        });
        summary.DeploymentRegressionStats.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["payment-api"] = 1, ["search-api"] = 1
        }, "only the deployment correlation (A, D) counts; B's fingerprint correlation does not");
    }

    [Fact]
    public async Task Summary_KeepsThePrototypeJsonPropertyNames()
    {
        var (host, api) = await ProjectAsync(Events());
        await using var _ = host;
        using var __ = api;

        using var doc = JsonDocument.Parse(await api.CreateClient().GetStringAsync("/api/v1/reports/summary"));

        doc.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal(
            "totalIncidents", "openIncidents", "resolvedIncidents", "meanTimeToAcknowledgeMinutes",
            "meanTimeToResolveMinutes", "severityBreakdown", "topFailingServices", "deploymentRegressionStats");
    }

    [Fact]
    public async Task SingleFigureRoutes_MatchTheSummary()
    {
        var (host, api) = await ProjectAsync(Events());
        await using var _ = host;
        using var __ = api;
        var client = api.CreateClient();

        (await client.GetFromJsonAsync<MinutesDto>("/api/v1/reports/mtta"))!.Minutes.Should().BeApproximately(20, 1e-9);
        (await client.GetFromJsonAsync<MinutesDto>("/api/v1/reports/mttr"))!.Minutes.Should().BeApproximately(90, 1e-9);
        (await client.GetFromJsonAsync<Dictionary<string, int>>("/api/v1/reports/severity-breakdown"))
            .Should().BeEquivalentTo(new Dictionary<string, int> { ["Critical"] = 2, ["High"] = 2, ["Low"] = 1 });
        (await client.GetFromJsonAsync<Dictionary<string, int>>("/api/v1/reports/top-failing-services"))!
            .Keys.Should().Equal("payment-api", "auth-api", "search-api");
        (await client.GetFromJsonAsync<Dictionary<string, int>>("/api/v1/reports/deployment-regressions"))
            .Should().BeEquivalentTo(new Dictionary<string, int> { ["payment-api"] = 1, ["search-api"] = 1 });
    }

    [Fact]
    public async Task NoIncidents_GivesZeroesAndEmptyDictionaries()
    {
        var (host, api) = await ProjectAsync();
        await using var _ = host;
        using var __ = api;

        var summary = await api.CreateClient().GetFromJsonAsync<SummaryDto>("/api/v1/reports/summary");

        summary.Should().BeEquivalentTo(new SummaryDto(0, 0, 0, 0, 0, [], [], []));
    }

    [Fact]
    public async Task TopFailingServices_IsTheTenWithMostIncidents_CountDescending()
    {
        // svc-01 three times, svc-02 twice, svc-03..svc-12 once: twelve services, ten reported,
        // ties broken by name so the cut is stable.
        var events = new List<object>
        {
            Created(Guid.NewGuid(), "svc-01", "prod", "High"),
            Created(Guid.NewGuid(), "svc-01", "prod", "High"),
            Created(Guid.NewGuid(), "svc-01", "prod", "High"),
            Created(Guid.NewGuid(), "svc-02", "prod", "High"),
            Created(Guid.NewGuid(), "svc-02", "prod", "High")
        };
        for (var i = 3; i <= 12; i++) events.Add(Created(Guid.NewGuid(), $"svc-{i:00}", "prod", "Low"));

        var (host, api) = await ProjectAsync(events.ToArray());
        await using var _ = host;
        using var __ = api;

        var top = await api.CreateClient().GetFromJsonAsync<Dictionary<string, int>>("/api/v1/reports/top-failing-services");

        top!.Keys.Should().Equal("svc-01", "svc-02", "svc-03", "svc-04", "svc-05", "svc-06", "svc-07", "svc-08",
            "svc-09", "svc-10");
        top["svc-01"].Should().Be(3);
        top["svc-02"].Should().Be(2);
    }

    [Theory]
    [InlineData("?serviceName=payment-api", 3, 1, 2, 20, 90)]
    [InlineData("?serviceName=PAYMENT-API", 3, 1, 2, 20, 90)]
    [InlineData("?environment=prod", 4, 1, 2, 15, 90)]
    [InlineData("?serviceName=payment-api&environment=staging", 1, 1, 0, 30, 0)]
    [InlineData("?serviceName=nobody", 0, 0, 0, 0, 0)]
    public async Task Filters_NarrowEveryFigure(string query, int total, int open, int resolved, double mtta, double mttr)
    {
        var (host, api) = await ProjectAsync(Events());
        await using var _ = host;
        using var __ = api;
        var client = api.CreateClient();

        var summary = await client.GetFromJsonAsync<SummaryDto>($"/api/v1/reports/summary{query}");

        summary!.TotalIncidents.Should().Be(total);
        summary.OpenIncidents.Should().Be(open);
        summary.ResolvedIncidents.Should().Be(resolved);
        summary.MeanTimeToAcknowledgeMinutes.Should().BeApproximately(mtta, 1e-9);
        summary.MeanTimeToResolveMinutes.Should().BeApproximately(mttr, 1e-9);
        (await client.GetFromJsonAsync<MinutesDto>($"/api/v1/reports/mtta{query}"))!.Minutes.Should().BeApproximately(mtta, 1e-9);
        (await client.GetFromJsonAsync<MinutesDto>($"/api/v1/reports/mttr{query}"))!.Minutes.Should().BeApproximately(mttr, 1e-9);
    }

    [Fact]
    public async Task EnvironmentFilter_NarrowsTheDictionaries()
    {
        var (host, api) = await ProjectAsync(Events());
        await using var _ = host;
        using var __ = api;

        var summary = await api.CreateClient().GetFromJsonAsync<SummaryDto>("/api/v1/reports/summary?environment=prod");

        summary!.SeverityBreakdown.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["Critical"] = 2, ["High"] = 1, ["Low"] = 1
        });
        summary.TopFailingServices.Keys.Should().Equal("payment-api", "auth-api", "search-api");
        summary.TopFailingServices["payment-api"].Should().Be(2);
        summary.DeploymentRegressionStats.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["payment-api"] = 1, ["search-api"] = 1
        });
    }

    [Fact]
    public async Task ServiceAndEnvironmentFilter_LeavesOnlyThatIncident()
    {
        var (host, api) = await ProjectAsync(Events());
        await using var _ = host;
        using var __ = api;

        var summary = await api.CreateClient()
            .GetFromJsonAsync<SummaryDto>("/api/v1/reports/summary?serviceName=payment-api&environment=staging");

        summary!.SeverityBreakdown.Should().BeEquivalentTo(new Dictionary<string, int> { ["High"] = 1 });
        summary.TopFailingServices.Should().BeEquivalentTo(new Dictionary<string, int> { ["payment-api"] = 1 });
        summary.DeploymentRegressionStats.Should().BeEmpty();
    }

    [Fact]
    public async Task ServiceReliability_HasOneRowPerServiceAndEnvironment()
    {
        var (host, api) = await ProjectAsync(Events());
        await using var _ = host;
        using var __ = api;

        var rows = await api.CreateClient().GetFromJsonAsync<List<ReliabilityRowDto>>("/api/v1/reports/service-reliability");

        rows.Should().Equal(
            new ReliabilityRowDto("payment-api", "prod", 2, 15, 90),
            new ReliabilityRowDto("auth-api", "prod", 1, 0, 0),
            new ReliabilityRowDto("payment-api", "staging", 1, 30, 0),
            new ReliabilityRowDto("search-api", "prod", 1, 0, 0));
    }

    [Fact]
    public async Task ServiceReliability_HonoursTheFilters()
    {
        var (host, api) = await ProjectAsync(Events());
        await using var _ = host;
        using var __ = api;
        var client = api.CreateClient();

        (await client.GetFromJsonAsync<List<ReliabilityRowDto>>("/api/v1/reports/service-reliability?serviceName=payment-api"))
            .Should().Equal(
                new ReliabilityRowDto("payment-api", "prod", 2, 15, 90),
                new ReliabilityRowDto("payment-api", "staging", 1, 30, 0));
        (await client.GetFromJsonAsync<List<ReliabilityRowDto>>("/api/v1/reports/service-reliability?environment=staging"))
            .Should().Equal(new ReliabilityRowDto("payment-api", "staging", 1, 30, 0));
    }

    [Fact]
    public async Task HealthEndpoints_Answer()
    {
        var (host, api) = await ProjectAsync();
        await using var _ = host;
        using var __ = api;

        (await api.CreateClient().GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
