using IncidentFlow.BuildingBlocks.Contracts;
using StackExchange.Redis;

namespace IncidentFlow.Services.Correlation.Worker.Correlation;

public sealed class CorrelationEngine(IConnectionMultiplexer redis)
{
    private const int Threshold = 2;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    // Returns one CorrelationMatch per correlation type that crossed the threshold.
    public async Task<IReadOnlyList<CorrelationMatch>> RecordAsync(IncidentCreated incident)
    {
        var db = redis.GetDatabase();
        var scoreNow = (double)incident.DetectedAt.ToUnixTimeMilliseconds();
        var scoreCutoff = (double)incident.DetectedAt.Subtract(Window).ToUnixTimeMilliseconds();
        var matches = new List<CorrelationMatch>();

        if (incident.DeploymentId is not null)
        {
            var key = $"corr:deploy:{incident.Environment}:{incident.DeploymentId}";
            await db.SortedSetAddAsync(key, incident.IncidentId.ToString(), scoreNow);
            await db.SortedSetRemoveRangeByScoreAsync(key, 0, scoreCutoff - 1);
            var members = await db.SortedSetRangeByScoreAsync(key, scoreCutoff, double.PositiveInfinity);
            if (members.Length >= Threshold)
                matches.Add(new CorrelationMatch("deployment", key, incident.DeploymentId, incident.Fingerprint,
                    members.Select(m => Guid.Parse(m!)).ToList()));
        }

        if (incident.Fingerprint is not null)
        {
            var key = $"corr:fingerprint:{incident.Environment}:{incident.Fingerprint}";
            await db.SortedSetAddAsync(key, incident.IncidentId.ToString(), scoreNow);
            await db.SortedSetRemoveRangeByScoreAsync(key, 0, scoreCutoff - 1);
            var members = await db.SortedSetRangeByScoreAsync(key, scoreCutoff, double.PositiveInfinity);
            if (members.Length >= Threshold)
                matches.Add(new CorrelationMatch("fingerprint", key, incident.DeploymentId, incident.Fingerprint,
                    members.Select(m => Guid.Parse(m!)).ToList()));
        }

        return matches;
    }
}

public sealed record CorrelationMatch(
    string Type,
    string Key,
    string? DeploymentId,
    string? Fingerprint,
    IReadOnlyList<Guid> IncidentIds);
