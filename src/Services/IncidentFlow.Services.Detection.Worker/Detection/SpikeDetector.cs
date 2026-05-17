namespace IncidentFlow.Services.Detection.Worker.Detection;

public sealed class SpikeDetector
{
    // keyed by "serviceName|environment|fingerprint"
    private readonly Dictionary<string, Queue<DateTimeOffset>> _windows = new();
    private readonly object _lock = new();

    private const int Threshold = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    public SpikeResult Record(string serviceName, string environment, string fingerprint, DateTimeOffset occurredAt)
    {
        var key = $"{serviceName}|{environment}|{fingerprint}";

        lock (_lock)
        {
            if (!_windows.TryGetValue(key, out var queue))
            {
                queue = new Queue<DateTimeOffset>();
                _windows[key] = queue;
            }

            var cutoff = occurredAt - Window;
            while (queue.Count > 0 && queue.Peek() < cutoff)
                queue.Dequeue();

            queue.Enqueue(occurredAt);

            if (queue.Count >= Threshold)
            {
                var signalCount = queue.Count;
                queue.Clear();
                return new SpikeResult(true, signalCount);
            }

            return new SpikeResult(false, queue.Count);
        }
    }
}

public sealed record SpikeResult(bool IsSpike, int Count);
