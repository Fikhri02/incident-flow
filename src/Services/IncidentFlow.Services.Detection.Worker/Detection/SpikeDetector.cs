using StackExchange.Redis;

namespace IncidentFlow.Services.Detection.Worker.Detection;

public sealed class SpikeDetector(IConnectionMultiplexer redis)
{
    private const int Threshold = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>A window idle this long holds nothing that still counts, so Redis may drop it.</summary>
    public static readonly TimeSpan KeyTtl = Window + TimeSpan.FromMinutes(1);

    // One script, so Redis runs it as a single step: add the signal, refresh the TTL, drop what fell out of
    // the window, count, and clear the window when it reaches the threshold. Signals consumed at the same
    // moment queue behind each other, so only the one that completes a set of five sees five.
    private const string RecordScript = """
        redis.call('ZADD', KEYS[1], ARGV[1], ARGV[2])
        redis.call('PEXPIRE', KEYS[1], ARGV[5])
        redis.call('ZREMRANGEBYSCORE', KEYS[1], 0, ARGV[3])
        local count = redis.call('ZCARD', KEYS[1])
        if count >= tonumber(ARGV[4]) then
            redis.call('DEL', KEYS[1])
            return {1, count}
        end
        return {0, count}
        """;

    public async Task<SpikeResult> RecordAsync(
        string serviceName, string environment, string fingerprint,
        DateTimeOffset occurredAt, Guid signalId)
    {
        var db = redis.GetDatabase();
        var key = $"spike:{serviceName}:{environment}:{fingerprint}";
        var scoreNow = occurredAt.ToUnixTimeMilliseconds();
        var lastExpired = occurredAt.Subtract(Window).ToUnixTimeMilliseconds() - 1;

        var result = (RedisResult[])(await db.ScriptEvaluateAsync(RecordScript, [key],
            [scoreNow, signalId.ToString(), lastExpired, Threshold, (long)KeyTtl.TotalMilliseconds]))!;

        return new SpikeResult((int)result[0] == 1, (int)result[1]);
    }
}

public sealed record SpikeResult(bool IsSpike, int Count);
