using FluentAssertions;
using IncidentFlow.IntegrationTests.Shared;
using IncidentFlow.Services.Correlation.Worker.Consumers;
using IncidentFlow.Services.Detection.Worker.Consumers;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IncidentFlow.IntegrationTests.Messaging;

public sealed record OutboxProbe(Guid Id, bool Fail);
public sealed record ProbeEcho(Guid Id);

/// <summary>Publishes like the worker consumers do (through the scoped IPublishEndpoint), then may fail.</summary>
public sealed class PublishThenMaybeThrowConsumer(IPublishEndpoint bus) : IConsumer<OutboxProbe>
{
    public async Task Consume(ConsumeContext<OutboxProbe> context)
    {
        await bus.Publish(new ProbeEcho(context.Message.Id), context.CancellationToken);
        if (context.Message.Fail) throw new InvalidOperationException("Failed after publishing.");
    }
}

/// <summary>Stands in for a downstream service. Outbox deliveries reach consumers, not harness.Published.</summary>
public sealed class ProbeEchoConsumer : IConsumer<ProbeEcho>
{
    public Task Consume(ConsumeContext<ProbeEcho> context) => Task.CompletedTask;
}

// Detection and Correlation have no database, so they use MassTransit's in-memory outbox: what a
// consumer publishes is held until the consume succeeds, and dropped if it throws.
public sealed class InMemoryOutboxTests
{
    public static TheoryData<string> Workers => new() { "detection", "correlation" };

    private static async Task<ServiceProvider> StartAsync(string worker)
    {
        var provider = new ServiceCollection()
            .AddLogging()
            .AddMassTransitTestHarness(x =>
            {
                if (worker == "detection") x.AddDetectionMessaging();
                else x.AddCorrelationMessaging();
                x.AddConsumer<PublishThenMaybeThrowConsumer>();
                x.AddConsumer<ProbeEchoConsumer>();
                x.UsingTestTransport(retry: false);
            })
            .BuildServiceProvider(true);
        await provider.GetRequiredService<ITestHarness>().Start();
        return provider;
    }

    [Theory]
    [MemberData(nameof(Workers))]
    public async Task ConsumerThatThrowsAfterPublishing_PublishesNothing(string worker)
    {
        await using var provider = await StartAsync(worker);
        var harness = provider.GetRequiredService<ITestHarness>();

        await harness.Bus.Publish(new OutboxProbe(Guid.NewGuid(), Fail: true));

        (await harness.Published.Any<Fault<OutboxProbe>>()).Should().BeTrue();
        await harness.InactivityTask;
        harness.Consumed.Select<ProbeEcho>().Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Workers))]
    public async Task ConsumerThatSucceeds_PublishesAfterwards(string worker)
    {
        await using var provider = await StartAsync(worker);
        var harness = provider.GetRequiredService<ITestHarness>();
        var id = Guid.NewGuid();

        await harness.Bus.Publish(new OutboxProbe(id, Fail: false));

        (await harness.Consumed.Any<ProbeEcho>(m => m.Context.Message.Id == id)).Should().BeTrue();
    }
}
