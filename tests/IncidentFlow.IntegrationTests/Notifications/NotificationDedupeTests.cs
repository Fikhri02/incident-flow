using System.Collections.Concurrent;
using FluentAssertions;
using IncidentFlow.BuildingBlocks.Contracts;
using IncidentFlow.IntegrationTests.Shared;
using IncidentFlow.Services.Notification.Worker.Consumers;
using IncidentFlow.Services.Notification.Worker.Notifications;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace IncidentFlow.IntegrationTests.Notifications;

// Notification has no database: it remembers MessageIds in Redis so a redelivered IncidentCreated does not
// post to Slack twice, while a post that failed, or whose worker died mid-post, is still posted later.
public sealed class NotificationDedupeTests(RedisFixture redis) : IClassFixture<RedisFixture>
{
    private sealed class RecordingNotifier : IIncidentNotifier
    {
        private int _failuresLeft;

        public RecordingNotifier(int failFirst = 0) => _failuresLeft = failFirst;

        public ConcurrentQueue<Guid> Calls { get; } = new();

        public Task NotifyAsync(IncidentCreated incident, CancellationToken cancellationToken)
        {
            Calls.Enqueue(incident.IncidentId);
            if (Interlocked.Decrement(ref _failuresLeft) >= 0)
                throw new HttpRequestException("Slack returned 503.");
            return Task.CompletedTask;
        }
    }

    /// <summary>Claims every message, then fails to give the claim back: Redis went away mid-message.</summary>
    private sealed class ReleaseFailsSentNotifications : ISentNotifications
    {
        public Task<NotificationClaim> TryClaimAsync(Guid messageId) => Task.FromResult(NotificationClaim.Claimed);

        public Task MarkSentAsync(Guid messageId) => Task.CompletedTask;

        public Task ReleaseAsync(Guid messageId) =>
            throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "Redis is unreachable.");
    }

    private async Task<ServiceProvider> StartAsync(IIncidentNotifier notifier, bool retry = true,
        ISentNotifications? sent = null)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(redis.Connection)
            .AddSingleton(notifier);
        // Registered first: AddNotificationMessaging only adds the Redis store when none is registered.
        if (sent is not null) services.AddSingleton(sent);
        var provider = services
            .AddMassTransitTestHarness(x =>
            {
                x.AddNotificationMessaging();
                x.UsingTestTransport(retry);
            })
            .BuildServiceProvider(true);
        await provider.GetRequiredService<ITestHarness>().Start();
        return provider;
    }

    private static IncidentCreated Incident() =>
        new(Guid.NewGuid(), "payment-api", "prod", null, "payment-timeout", null, "high", "ErrorSpike",
            "5 errors in 5 min", DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RedeliveredIncident_PostsToSlackOnce()
    {
        var notifier = new RecordingNotifier();
        await using var provider = await StartAsync(notifier);
        var harness = provider.GetRequiredService<ITestHarness>();
        var incident = Incident();
        var messageId = NewId.NextGuid();

        await harness.Bus.Publish(incident, ctx => ctx.MessageId = messageId);
        await harness.Bus.Publish(incident, ctx => ctx.MessageId = messageId);
        await harness.InactivityTask;

        notifier.Calls.Where(id => id == incident.IncidentId).Should().HaveCount(1);
    }

    [Fact]
    public async Task DistinctMessages_EachPost()
    {
        var notifier = new RecordingNotifier();
        await using var provider = await StartAsync(notifier);
        var harness = provider.GetRequiredService<ITestHarness>();
        var incident = Incident();

        await harness.Bus.Publish(incident);
        await harness.Bus.Publish(incident);
        await harness.InactivityTask;

        notifier.Calls.Where(id => id == incident.IncidentId).Should().HaveCount(2);
    }

    [Fact]
    public async Task FailedPost_IsRetried_AndNotMarkedAsSent()
    {
        var notifier = new RecordingNotifier(failFirst: 1);
        await using var provider = await StartAsync(notifier);
        var harness = provider.GetRequiredService<ITestHarness>();
        var incident = Incident();

        await harness.Bus.Publish(incident);
        await harness.InactivityTask;

        notifier.Calls.Where(id => id == incident.IncidentId).Should().HaveCount(2, "the first post failed, the retry posts");
        harness.Published.Select<Fault<IncidentCreated>>().Should().BeEmpty();
    }

    [Fact]
    public async Task SentMarker_ExpiresAfterADay()
    {
        var notifier = new RecordingNotifier();
        await using var provider = await StartAsync(notifier);
        var harness = provider.GetRequiredService<ITestHarness>();
        var messageId = NewId.NextGuid();

        await harness.Bus.Publish(Incident(), ctx => ctx.MessageId = messageId);
        await harness.InactivityTask;

        var server = redis.Connection.GetServer(redis.Connection.GetEndPoints()[0]);
        var key = server.Keys(pattern: $"*{messageId}*").Should().ContainSingle().Subject;
        var ttl = await redis.Connection.GetDatabase().KeyTimeToLiveAsync(key);
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeGreaterThan(TimeSpan.FromHours(23)).And.BeLessThanOrEqualTo(TimeSpan.FromHours(24));
    }

    private static string Key(Guid messageId) => $"notification:sent:{messageId}";

    // A worker claimed the message and died before posting (or before recording the post). The broker
    // redelivers it: that delivery must not be acked as "already sent", or the notification is lost.
    [Fact]
    public async Task RedeliveryWhileClaimIsInFlight_IsRetried_AndPostsOnceTheClaimExpires()
    {
        var notifier = new RecordingNotifier();
        await using var provider = await StartAsync(notifier);
        var harness = provider.GetRequiredService<ITestHarness>();
        var incident = Incident();
        var messageId = NewId.NextGuid();
        await redis.Connection.GetDatabase().StringSetAsync(Key(messageId), "in-flight", TimeSpan.FromSeconds(2));

        await harness.Bus.Publish(incident, ctx => ctx.MessageId = messageId);

        // Retries come after 200 ms, 1 s and 5 s: the last one runs after the dead worker's claim expired.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (notifier.Calls.IsEmpty && DateTime.UtcNow < deadline)
            await Task.Delay(100);

        notifier.Calls.Where(id => id == incident.IncidentId).Should().HaveCount(1,
            "the redelivery waits out the dead worker's in-flight claim and then posts");
        (await redis.Connection.GetDatabase().StringGetAsync(Key(messageId))).ToString().Should().Be("sent");
    }

    [Fact]
    public async Task RedeliveryWhileClaimIsInFlight_Faults_InsteadOfBeingSkipped()
    {
        var notifier = new RecordingNotifier();
        await using var provider = await StartAsync(notifier, retry: false);
        var harness = provider.GetRequiredService<ITestHarness>();
        var messageId = NewId.NextGuid();
        await redis.Connection.GetDatabase().StringSetAsync(Key(messageId), "in-flight", TimeSpan.FromMinutes(1));

        await harness.Bus.Publish(Incident(), ctx => ctx.MessageId = messageId);

        (await harness.Published.Any<Fault<IncidentCreated>>()).Should().BeTrue(
            "an in-flight claim is not proof of a post: the message goes back for a later retry or to _error");
        notifier.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ClaimWhilePosting_IsShortLived_ThenBecomesSentForADay()
    {
        var messageId = NewId.NextGuid();
        TimeSpan? ttlWhilePosting = null;
        string? valueWhilePosting = null;
        var notifier = new InspectingNotifier(async () =>
        {
            var db = redis.Connection.GetDatabase();
            ttlWhilePosting = await db.KeyTimeToLiveAsync(Key(messageId));
            valueWhilePosting = await db.StringGetAsync(Key(messageId));
        });
        await using var provider = await StartAsync(notifier);
        var harness = provider.GetRequiredService<ITestHarness>();

        await harness.Bus.Publish(Incident(), ctx => ctx.MessageId = messageId);
        await harness.InactivityTask;

        valueWhilePosting.Should().Be("in-flight");
        ttlWhilePosting.Should().NotBeNull();
        ttlWhilePosting!.Value.Should().BeGreaterThan(TimeSpan.FromSeconds(60))
            .And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(90), "a crashed worker's claim must free up quickly");
        (await redis.Connection.GetDatabase().StringGetAsync(Key(messageId))).ToString().Should().Be("sent");
    }

    [Fact]
    public async Task ReleaseFailure_StillSurfacesTheOriginalException()
    {
        var notifier = new RecordingNotifier(failFirst: 1);
        await using var provider = await StartAsync(notifier, retry: false, sent: new ReleaseFailsSentNotifications());
        var harness = provider.GetRequiredService<ITestHarness>();

        await harness.Bus.Publish(Incident());

        (await harness.Published.Any<Fault<IncidentCreated>>()).Should().BeTrue();
        var fault = harness.Published.Select<Fault<IncidentCreated>>().First().Context.Message;
        fault.Exceptions.Should().ContainSingle()
            .Which.ExceptionType.Should().Be(typeof(HttpRequestException).FullName,
                "the failed post is the error to retry and report, not the failed clean-up");
    }

    private sealed class InspectingNotifier(Func<Task> whilePosting) : IIncidentNotifier
    {
        public Task NotifyAsync(IncidentCreated incident, CancellationToken cancellationToken) => whilePosting();
    }
}
