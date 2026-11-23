using System.Linq.Expressions;
using System.Text.Json;
using IncidentFlow.Services.Incident.Api.Domain.Events;

namespace IncidentFlow.Services.Incident.Api.Domain;

/// <summary>
/// Aggregate root for one incident. Every state change goes through a method here,
/// which enforces the lifecycle rules and records a domain event.
/// Lifecycle: open -> acknowledged -> resolved -> closed; resolve is allowed from open,
/// and close from any status except closed.
/// </summary>
public sealed class Incident
{
    private readonly List<IIncidentDomainEvent> _domainEvents = [];

    private Incident() { } // EF Core

    public Guid IncidentId { get; private set; }
    public string ServiceName { get; private set; } = string.Empty;
    public string Environment { get; private set; } = string.Empty;
    public string? Region { get; private set; }
    public string? Fingerprint { get; private set; }
    public string? DeploymentId { get; private set; }
    public string Severity { get; private set; } = string.Empty;
    public string RuleName { get; private set; } = string.Empty;
    public string Summary { get; private set; } = string.Empty;
    public IncidentStatus Status { get; private set; }
    public DateTimeOffset DetectedAt { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public string? AcknowledgedBy { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? ResolvedBy { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string SignalIdsJson { get; private set; } = "[]";
    public DateTimeOffset CreatedAt { get; private set; }

    public IncidentKey Key => new(ServiceName, Environment, Fingerprint);

    public IReadOnlyList<Guid> SignalIds =>
        JsonSerializer.Deserialize<List<Guid>>(SignalIdsJson) ?? [];

    public IReadOnlyList<IIncidentDomainEvent> DomainEvents => _domainEvents;

    /// <summary>Incidents still being worked: new anomalies with the same key fold into these.</summary>
    public static Expression<Func<Incident, bool>> IsActive =>
        i => i.Status == IncidentStatus.Open || i.Status == IncidentStatus.Acknowledged;

    public static Expression<Func<Incident, bool>> IsActiveFor(IncidentKey key)
    {
        var serviceName = key.ServiceName;
        var environment = key.Environment;
        var fingerprint = key.Fingerprint;
        return i => i.ServiceName == serviceName
                    && i.Environment == environment
                    && i.Fingerprint == fingerprint
                    && (i.Status == IncidentStatus.Open || i.Status == IncidentStatus.Acknowledged);
    }

    public static Incident Open(IncidentKey key, IncidentDetails details, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(details);
        if (string.IsNullOrWhiteSpace(details.CorrelationId))
            throw new ArgumentException("Correlation id is required.", nameof(details));

        var incident = new Incident
        {
            IncidentId = Guid.NewGuid(),
            ServiceName = key.ServiceName,
            Environment = key.Environment,
            Fingerprint = key.Fingerprint,
            Region = details.Region,
            DeploymentId = details.DeploymentId,
            Severity = details.Severity,
            RuleName = details.RuleName,
            Summary = details.Summary,
            Status = IncidentStatus.Open,
            DetectedAt = details.DetectedAt,
            CorrelationId = details.CorrelationId,
            SignalIdsJson = JsonSerializer.Serialize(details.SignalIds),
            CreatedAt = now
        };

        incident._domainEvents.Add(new IncidentWasOpened(incident.IncidentId, now));
        return incident;
    }

    public void Acknowledge(string by, DateTimeOffset now)
    {
        if (Status != IncidentStatus.Open)
            throw new IncidentTransitionException($"Cannot acknowledge incident in status '{Status.ToWire()}'.");
        RequirePerson(by, "Acknowledged by");

        Status = IncidentStatus.Acknowledged;
        AcknowledgedAt = now;
        AcknowledgedBy = by;
        _domainEvents.Add(new IncidentWasAcknowledged(IncidentId, by, now));
    }

    public void Resolve(string by, DateTimeOffset now)
    {
        if (Status is IncidentStatus.Resolved or IncidentStatus.Closed)
            throw new IncidentTransitionException($"Incident is already '{Status.ToWire()}'.");
        RequirePerson(by, "Resolved by");

        Status = IncidentStatus.Resolved;
        ResolvedAt = now;
        ResolvedBy = by;
        _domainEvents.Add(new IncidentWasResolved(IncidentId, by, now));
    }

    public void Close(DateTimeOffset now)
    {
        if (Status == IncidentStatus.Closed)
            throw new IncidentTransitionException("Incident is already closed.");

        Status = IncidentStatus.Closed;
        ClosedAt = now;
        _domainEvents.Add(new IncidentWasClosed(IncidentId, now));
    }

    /// <summary>Hands the recorded events to the caller (to publish through the outbox) and clears them.</summary>
    public IReadOnlyList<IIncidentDomainEvent> DequeueDomainEvents()
    {
        var events = _domainEvents.ToList();
        _domainEvents.Clear();
        return events;
    }

    private static void RequirePerson(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{field} is required.");
    }
}
