using IncidentFlow.BuildingBlocks.EventBus;

namespace IncidentFlow.IntegrationTests.Shared;

public sealed class NullEventBus : IEventBus
{
    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class => Task.CompletedTask;
}
