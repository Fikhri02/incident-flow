namespace IncidentFlow.Services.Incident.Api.Domain.Events;

/// <summary>
/// Something that happened to an incident, recorded by the aggregate itself.
/// These stay inside the Incident service; <c>IncidentEventPublisher</c> translates them
/// into the shared integration contracts other services consume.
/// </summary>
public interface IIncidentDomainEvent
{
    Guid IncidentId { get; }
    DateTimeOffset OccurredAt { get; }
}

public sealed record IncidentWasOpened(Guid IncidentId, DateTimeOffset OccurredAt) : IIncidentDomainEvent;

public sealed record IncidentWasAcknowledged(Guid IncidentId, string By, DateTimeOffset OccurredAt) : IIncidentDomainEvent;

public sealed record IncidentWasResolved(Guid IncidentId, string By, DateTimeOffset OccurredAt) : IIncidentDomainEvent;

public sealed record IncidentWasClosed(Guid IncidentId, DateTimeOffset OccurredAt) : IIncidentDomainEvent;
