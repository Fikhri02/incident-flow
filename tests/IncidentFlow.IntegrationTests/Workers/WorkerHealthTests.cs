using System.Net;
using FluentAssertions;
using IncidentFlow.IntegrationTests.Shared;
using IncidentFlow.Services.Correlation.Worker.Correlation;
using IncidentFlow.Services.Detection.Worker.Detection;
using IncidentFlow.Services.Notification.Worker.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace IncidentFlow.IntegrationTests.Workers;

/// <summary>
/// Hosts a worker in memory. TEntry is any public type from the worker's assembly: the workers'
/// top-level Program classes are internal and would clash with the APIs' Program anyway.
/// </summary>
public sealed class WorkerFactory<TEntry>(string redisConnectionString) : WebApplicationFactory<TEntry>
    where TEntry : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:redis", redisConnectionString);
        // The bus is built (its health check resolves it) but never started.
        builder.UseSetting("ConnectionStrings:rabbitmq", "amqp://guest:guest@localhost:5672");
        builder.ConfigureServices(services => services.RemoveMassTransitHostedServices());
    }
}

// Every worker answers the same health endpoints as the APIs, so an orchestrator can probe it.
public sealed class WorkerHealthTests(RedisFixture redis) : IClassFixture<RedisFixture>
{
    private async Task AssertHealthEndpoints<TEntry>() where TEntry : class
    {
        await using var factory = new WorkerFactory<TEntry>(redis.ConnectionString);
        var client = factory.CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        // Ready includes the bus, which is not started here; the endpoint must exist all the same.
        (await client.GetAsync("/health/ready")).StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public Task Detection_ExposesHealthEndpoints() => AssertHealthEndpoints<SpikeDetector>();

    [Fact]
    public Task Correlation_ExposesHealthEndpoints() => AssertHealthEndpoints<CorrelationEngine>();

    [Fact]
    public Task Notification_ExposesHealthEndpoints() => AssertHealthEndpoints<SlackNotifier>();
}
