using FluentAssertions;
using IncidentFlow.BuildingBlocks.Contracts;
using IncidentFlow.IntegrationTests.Shared;
using IncidentFlow.Services.Timeline.Api.Consumers;
using IncidentFlow.Services.Timeline.Api.Data;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IncidentFlow.IntegrationTests.Timeline;

// Timeline's consumers write to their own database, so a redelivered message (same MessageId)
// must be recognised by the inbox and skipped, not appended a second time.
public sealed class TimelineInboxTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private async Task<ServiceProvider> StartAsync()
    {
        var database = postgres.NewDatabase();
        var provider = new ServiceCollection()
            .AddLogging()
            .AddDbContext<TimelineDbContext>(o => o.UseNpgsql(database))
            .AddMassTransitTestHarness(x =>
            {
                x.AddTimelineMessaging();
                x.UsingTestTransport();
            })
            .BuildServiceProvider(true);

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TimelineDbContext>().Database.MigrateAsync();

        await provider.GetRequiredService<ITestHarness>().Start();
        return provider;
    }

    [Fact]
    public async Task RedeliveredMessage_AppendsOneRow()
    {
        await using var provider = await StartAsync();
        var harness = provider.GetRequiredService<ITestHarness>();
        var correlationId = Guid.NewGuid().ToString("N");
        var signal = new ErrorSignalReceived(Guid.NewGuid(), "test", "payment-api", "prod", null,
            "payment-timeout", "Timeout", null, null, DateTimeOffset.UtcNow, correlationId);
        var messageId = NewId.NextGuid();

        await harness.Bus.Publish(signal, ctx => ctx.MessageId = messageId);
        await harness.Bus.Publish(signal, ctx => ctx.MessageId = messageId);
        await harness.InactivityTask;

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TimelineDbContext>();
        (await db.Entries.CountAsync(e => e.CorrelationId == correlationId)).Should().Be(1);
    }

    [Fact]
    public async Task DistinctMessages_EachAppendARow()
    {
        await using var provider = await StartAsync();
        var harness = provider.GetRequiredService<ITestHarness>();
        var correlationId = Guid.NewGuid().ToString("N");
        var signal = new ErrorSignalReceived(Guid.NewGuid(), "test", "payment-api", "prod", null,
            "payment-timeout", "Timeout", null, null, DateTimeOffset.UtcNow, correlationId);

        await harness.Bus.Publish(signal);
        await harness.Bus.Publish(signal);
        await harness.InactivityTask;

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TimelineDbContext>();
        (await db.Entries.CountAsync(e => e.CorrelationId == correlationId)).Should().Be(2);
    }
}
