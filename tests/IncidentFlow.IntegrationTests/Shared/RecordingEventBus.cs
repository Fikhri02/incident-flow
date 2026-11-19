using System.Collections.Concurrent;
using IncidentFlow.BuildingBlocks.EventBus;

namespace IncidentFlow.IntegrationTests.Shared;

/// <summary>Keeps every published message so tests can assert on integration events.</summary>
public sealed class RecordingEventBus : IEventBus
{
    public ConcurrentQueue<object> Published { get; } = new();

    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class
    {
        Published.Enqueue(message);
        return Task.CompletedTask;
    }
}
