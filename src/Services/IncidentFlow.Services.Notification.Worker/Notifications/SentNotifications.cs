using StackExchange.Redis;

namespace IncidentFlow.Services.Notification.Worker.Notifications;

public enum NotificationClaim
{
    /// <summary>This delivery owns the message now: post, then mark it sent.</summary>
    Claimed,

    /// <summary>Another delivery claimed it and has not finished (or died before finishing). Not proof of a post.</summary>
    InFlight,

    /// <summary>Already posted: skip.</summary>
    Sent
}

/// <summary>Remembers which messages already produced a notification, so a redelivery does not post again.</summary>
public interface ISentNotifications
{
    /// <summary>Claims the message for <see cref="RedisSentNotifications.InFlightTtl"/> if nobody holds it.</summary>
    Task<NotificationClaim> TryClaimAsync(Guid messageId);

    /// <summary>Records the post for <see cref="RedisSentNotifications.SentTtl"/>, so redeliveries skip it.</summary>
    Task MarkSentAsync(Guid messageId);

    /// <summary>Gives the claim back after a failed post, so the retry can post.</summary>
    Task ReleaseAsync(Guid messageId);
}

/// <summary>
/// One Redis key per MessageId with two states. The claim is "in-flight" (SET NX) with a short TTL, longer
/// than the slowest post through the standard resilience handler (30 s total timeout); after the post the
/// key becomes "sent" for a day, long enough to cover broker redeliveries and _error-queue replays.
/// If a worker dies between claim and "sent", the claim expires and a later delivery posts: at-least-once.
/// </summary>
public sealed class RedisSentNotifications(IConnectionMultiplexer redis) : ISentNotifications
{
    public static readonly TimeSpan InFlightTtl = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan SentTtl = TimeSpan.FromHours(24);

    public const string InFlightValue = "in-flight";
    public const string SentValue = "sent";

    private static RedisKey Key(Guid messageId) => $"notification:sent:{messageId}";

    public async Task<NotificationClaim> TryClaimAsync(Guid messageId)
    {
        var db = redis.GetDatabase();
        // Two rounds: the key can expire between a failed SET NX and the GET.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (await db.StringSetAsync(Key(messageId), InFlightValue, InFlightTtl, When.NotExists))
                return NotificationClaim.Claimed;

            var value = await db.StringGetAsync(Key(messageId));
            if (value.IsNull) continue;
            // Markers written before the two-state claim hold a timestamp: those were sent.
            return value == InFlightValue ? NotificationClaim.InFlight : NotificationClaim.Sent;
        }

        return NotificationClaim.InFlight;
    }

    public Task MarkSentAsync(Guid messageId) =>
        redis.GetDatabase().StringSetAsync(Key(messageId), SentValue, SentTtl);

    public Task ReleaseAsync(Guid messageId) => redis.GetDatabase().KeyDeleteAsync(Key(messageId));
}

/// <summary>
/// Thrown when another delivery holds the claim: MassTransit retries the message, and once the claim
/// expires (its holder died) or turns into "sent", the retry posts or skips. Out of retries, it goes to _error.
/// </summary>
public sealed class NotificationInFlightException(Guid messageId)
    : Exception($"Message {messageId} is claimed by another delivery that has not finished posting.");
