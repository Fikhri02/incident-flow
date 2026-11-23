using FluentAssertions;
using IncidentFlow.BuildingBlocks.Contracts;
using IncidentFlow.IntegrationTests.Shared;
using IncidentFlow.Services.Detection.Worker.Consumers;
using IncidentFlow.Services.Detection.Worker.Detection;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace IncidentFlow.IntegrationTests.Detection;

// Detection's real consumer and registration on the in-memory transport with real Redis.
public sealed class DetectionMessagingTests(RedisFixture redis) : IClassFixture<RedisFixture>
{
    /// <summary>Stands in for Incident. The in-memory outbox delivers to consumers, not to harness.Published.</summary>
    private sealed class AnomalyProbe : IConsumer<AnomalyDetected>
    {
        public Task Consume(ConsumeContext<AnomalyDetected> context) => Task.CompletedTask;
    }

    [Fact]
    public async Task Spike_PublishesAnomaly_WithTheSignalCorrelationIdInTheEnvelope()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(redis.Connection)
            .AddSingleton<SpikeDetector>()
            .AddMassTransitTestHarness(x =>
            {
                x.AddDetectionMessaging();
                x.AddConsumer<AnomalyProbe>();
                x.UsingTestTransport();
            })
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var correlationId = Guid.NewGuid();
        var fingerprint = $"fp-{Guid.NewGuid():N}";

        for (var i = 0; i < 5; i++)
            await harness.Bus.Publish(new ErrorSignalReceived(Guid.NewGuid(), "test", "payment-api", "prod", null,
                fingerprint, "Timeout", null, null, DateTimeOffset.UtcNow.AddSeconds(i), correlationId.ToString("N")));

        (await harness.Consumed.Any<AnomalyDetected>(m => m.Context.Message.Fingerprint == fingerprint)).Should().BeTrue();
        harness.Consumed.Select<AnomalyDetected>().Where(m => m.Context.Message.Fingerprint == fingerprint)
            .Should().OnlyContain(m => m.Context.CorrelationId == correlationId);
    }
}
