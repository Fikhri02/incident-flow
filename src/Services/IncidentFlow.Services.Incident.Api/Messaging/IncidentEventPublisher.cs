using IncidentFlow.BuildingBlocks.Contracts;
using IncidentFlow.BuildingBlocks.EventBus;
using IncidentFlow.Services.Incident.Api.Domain.Events;

namespace IncidentFlow.Services.Incident.Api.Messaging;

/// <summary>
/// Translates the aggregate's domain events into the integration contracts in BuildingBlocks
/// and publishes them. Call after the aggregate has been saved.
/// </summary>
public sealed class IncidentEventPublisher(IEventBus bus)
{
    public async Task PublishAsync(Domain.Incident incident, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in incident.DequeueDomainEvents())
        {
            switch (domainEvent)
            {
                case IncidentWasOpened:
                    await bus.PublishAsync(new IncidentCreated(
                        incident.IncidentId, incident.ServiceName, incident.Environment,
                        incident.Region, incident.Fingerprint, incident.DeploymentId,
                        incident.Severity, incident.RuleName, incident.Summary,
                        incident.DetectedAt, incident.CorrelationId), cancellationToken);
                    break;

                case IncidentWasAcknowledged e:
                    await bus.PublishAsync(new IncidentAcknowledged(
                        incident.IncidentId, incident.ServiceName, incident.Environment,
                        e.By, e.OccurredAt, incident.CorrelationId), cancellationToken);
                    break;

                case IncidentWasResolved e:
                    await bus.PublishAsync(new IncidentResolved(
                        incident.IncidentId, incident.ServiceName, incident.Environment,
                        e.By, e.OccurredAt, incident.CorrelationId), cancellationToken);
                    break;

                case IncidentWasClosed e:
                    await bus.PublishAsync(new IncidentClosed(
                        incident.IncidentId, incident.ServiceName, incident.Environment,
                        e.OccurredAt, incident.CorrelationId), cancellationToken);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"No integration event mapped for {domainEvent.GetType().Name}.");
            }
        }
    }
}
