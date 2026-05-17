using IncidentFlow.BuildingBlocks.EventBus;
using MassTransit;

namespace IncidentFlow.Services.Incident.Api.Messaging;

public sealed class MassTransitEventBus(IPublishEndpoint publishEndpoint) : IEventBus
{
    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class =>
        publishEndpoint.Publish(message, cancellationToken);
}
