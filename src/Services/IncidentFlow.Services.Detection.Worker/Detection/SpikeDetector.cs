using StackExchange.Redis;

namespace IncidentFlow.Services.Detection.Worker.Detection;

public sealed class SpikeDetector(IConnectionMultiplexer redis)
{
    private const int Threshold = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>A window idle this long holds nothing that still counts, so Redis may drop it.</summary>
    public static readonly TimeSpan KeyTtl = Window + TimeSpan.FromMinutes(1);

    public async Task<SpikeResult> RecordAsync(
        string serviceName, string environment, string fingerprint,
        DateTimeOffset occurredAt, Guid signalId)
    {
        var db = redis.GetDatabase();
        var key = $"spike:{serviceName}:{environment}:{fingerprint}";
        var scoreNow = (double)occurredAt.ToUnixTimeMilliseconds();
        var scoreCutoff = (double)occurredAt.Subtract(Window).ToUnixTimeMilliseconds();

        await db.SortedSetAddAsync(key, signalId.ToString(), scoreNow);
        await db.KeyExpireAsync(key, KeyTtl);
        await db.SortedSetRemoveRangeByScoreAsync(key, 0, scoreCutoff - 1);
        var count = await db.SortedSetLengthAsync(key);

        if (count >= Threshold)
        {
            await db.KeyDeleteAsync(key);
            return new SpikeResult(true, (int)count);
        }

        return new SpikeResult(false, (int)count);
    }
}

public sealed record SpikeResult(bool IsSpike, int Count);
