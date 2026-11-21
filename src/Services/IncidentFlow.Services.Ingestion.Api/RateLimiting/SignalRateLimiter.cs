using System.Threading.RateLimiting;

namespace IncidentFlow.Services.Ingestion.Api.RateLimiting;

/// <summary>Anything that names the system, service and environment a signal came from.</summary>
public interface ISignalSource
{
    string SourceSystem { get; }
    string ServiceName { get; }
    string Environment { get; }
}

/// <summary>
/// The engine's rule on the real ingestion path: each source (system, service, environment,
/// case-insensitive) may send <see cref="PermitsPerMinute"/> signals per fixed one-minute window.
/// The limit lives here rather than at the gateway because the source is in the request body,
/// which YARP forwards without reading.
/// Counts are per Ingestion instance; running several instances would need a shared counter (Redis).
/// </summary>
public sealed class SignalRateLimiter : IDisposable
{
    public const string PermitsSetting = "Ingestion:RateLimit:PermitsPerMinute";
    public const int DefaultPermitsPerMinute = 1000;

    private readonly PartitionedRateLimiter<string> _limiter;

    public SignalRateLimiter(IConfiguration configuration)
    {
        PermitsPerMinute = configuration.GetValue(PermitsSetting, DefaultPermitsPerMinute);
        _limiter = PartitionedRateLimiter.Create<string, string>(
            key => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = PermitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }),
            StringComparer.OrdinalIgnoreCase);
    }

    public int PermitsPerMinute { get; }

    /// <returns>False when the source has used its window; <paramref name="retryAfter"/> says when it resets.</returns>
    public bool TryAcquire(ISignalSource source, out TimeSpan retryAfter)
    {
        using var lease = _limiter.AttemptAcquire($"{source.SourceSystem}\n{source.ServiceName}\n{source.Environment}");
        if (lease.IsAcquired)
        {
            retryAfter = TimeSpan.Zero;
            return true;
        }

        retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromMinutes(1);
        return false;
    }

    public void Dispose() => _limiter.Dispose();
}
