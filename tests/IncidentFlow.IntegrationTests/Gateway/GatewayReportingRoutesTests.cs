extern alias gateway;

using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using GatewayProgram = gateway::Program;

namespace IncidentFlow.IntegrationTests.Gateway;

/// <summary>
/// A stand-in for the Reporting service on a real port: records the method, path and query of every
/// request YARP forwards to it.
/// </summary>
public sealed class StubReportingBackend : IAsyncLifetime
{
    private WebApplication _app = null!;

    public ConcurrentQueue<string> Received { get; } = new();

    public string Url { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();
        _app.Map("/api/v1/reports/{**rest}", (HttpContext ctx) =>
        {
            Received.Enqueue($"{ctx.Request.Method} {ctx.Request.Path}{ctx.Request.QueryString}");
            return Results.Ok(new { servedBy = "reporting-stub" });
        });
        await _app.StartAsync();
        Url = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();
}

public sealed class ReportingGatewayFactory(string reportingUrl) : WebApplicationFactory<GatewayProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("services:reporting:http:0", reportingUrl);
}

// Reports are served by the Reporting service; the gateway only proxies them. The in-process prototype
// (IncidentFlow.Core) and every route it answered are gone.
public sealed class GatewayReportingRoutesTests(StubReportingBackend reporting) : IClassFixture<StubReportingBackend>
{
    public static TheoryData<string, string> ReportRoutes => new()
    {
        { "GET", "/api/v1/reports/summary" },
        { "GET", "/api/v1/reports/mtta" },
        { "GET", "/api/v1/reports/mttr" },
        { "GET", "/api/v1/reports/severity-breakdown" },
        { "GET", "/api/v1/reports/top-failing-services" },
        { "GET", "/api/v1/reports/service-reliability" },
        { "GET", "/api/v1/reports/deployment-regressions" },
        { "POST", "/api/v1/reports/rebuild" },
    };

    public static TheoryData<string, string> RetiredCoreRoutes => new()
    {
        { "GET", "/api/v1/events" },
        { "GET", "/api/v1/events/outbox" },
        { "GET", "/api/v1/events/inbox" },
        { "POST", "/api/v1/notifications/retry-failed" },
        { "GET", "/api/v1/runtime/readiness" },
    };

    [Theory]
    [MemberData(nameof(ReportRoutes))]
    public async Task ReportRoute_IsProxiedToTheReportingService(string method, string path)
    {
        await using var factory = new ReportingGatewayFactory(reporting.Url);
        var client = factory.CreateClient();
        reporting.Received.Clear();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("reporting-stub");
        reporting.Received.Should().Equal($"{method} {path}");
    }

    [Fact]
    public async Task ReportFilters_TravelWithTheProxiedRequest()
    {
        await using var factory = new ReportingGatewayFactory(reporting.Url);
        var client = factory.CreateClient();
        reporting.Received.Clear();

        var response = await client.GetAsync("/api/v1/reports/summary?serviceName=payments&environment=prod");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        reporting.Received.Should().Equal("GET /api/v1/reports/summary?serviceName=payments&environment=prod");
    }

    [Theory]
    [MemberData(nameof(RetiredCoreRoutes))]
    public async Task RetiredCoreRoute_IsNotFound(string method, string path)
    {
        await using var factory = new ReportingGatewayFactory(reporting.Url);
        var client = factory.CreateClient();
        reporting.Received.Clear();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        reporting.Received.Should().BeEmpty();
    }

    [Fact]
    public async Task Root_DescribesTheWorkflowThatRuns_WithoutUnbuiltCapabilities()
    {
        await using var factory = new ReportingGatewayFactory(reporting.Url);
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/");

        body.Should().NotContainEquivalentOf("postmortem");
        body.Should().NotContainEquivalentOf("recommendation");
        body.Should().Contain("reporting");
    }
}
