extern alias gateway;

using System.Collections.Concurrent;
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
/// A stand-in for the Incident API on a real port: records the x-correlation-id values YARP forwards
/// and echoes the id on its response, as the real services do.
/// </summary>
public sealed class StubBackend : IAsyncLifetime
{
    private WebApplication _app = null!;

    public ConcurrentQueue<string[]> ForwardedIds { get; } = new();

    public string Url { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();
        _app.Map("/api/v1/incidents/{**rest}", (HttpContext ctx) =>
        {
            var ids = ctx.Request.Headers["x-correlation-id"].Select(v => v ?? "").ToArray();
            ForwardedIds.Enqueue(ids);
            if (ids.Length > 0) ctx.Response.Headers["x-correlation-id"] = ids[0];
            return Results.Ok(new { ok = true });
        });
        await _app.StartAsync();
        Url = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();
}

public sealed class GatewayFactory(string incidentUrl) : WebApplicationFactory<GatewayProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("services:incident:http:0", incidentUrl);
}

// The gateway is where a request gets its correlation id. The id has to travel with the proxied
// request (the services put it on the events they publish) and come back to the client exactly once.
public sealed class GatewayCorrelationIdTests(StubBackend backend) : IClassFixture<StubBackend>
{
    private const string Header = "x-correlation-id";

    [Fact]
    public async Task RequestWithoutAnId_GetsOne_ForwardedToTheService_AndReturnedOnce()
    {
        await using var factory = new GatewayFactory(backend.Url);
        var client = factory.CreateClient();
        backend.ForwardedIds.Clear();

        var response = await client.GetAsync($"/api/v1/incidents/{Guid.NewGuid()}");

        response.IsSuccessStatusCode.Should().BeTrue();
        backend.ForwardedIds.Should().ContainSingle();
        var forwarded = backend.ForwardedIds.Single().Should().ContainSingle("the gateway's id goes with the proxied request").Subject;
        forwarded.Should().NotBeNullOrWhiteSpace();
        response.Headers.GetValues(Header).Should().Equal([forwarded], "the client sees the same id, once");
    }

    [Fact]
    public async Task RequestWithAnId_ForwardsThatId_AndReturnsItOnce()
    {
        await using var factory = new GatewayFactory(backend.Url);
        var client = factory.CreateClient();
        backend.ForwardedIds.Clear();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/incidents/{Guid.NewGuid()}");
        request.Headers.Add(Header, "client-supplied-id");

        var response = await client.SendAsync(request);

        response.IsSuccessStatusCode.Should().BeTrue();
        backend.ForwardedIds.Single().Should().Equal("client-supplied-id");
        response.Headers.GetValues(Header).Should().Equal("client-supplied-id");
    }

    [Fact]
    public async Task GatewayOwnRoute_ReturnsAnId()
    {
        await using var factory = new GatewayFactory(backend.Url);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        response.Headers.GetValues(Header).Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
    }
}
