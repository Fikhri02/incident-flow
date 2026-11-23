namespace IncidentFlow.BuildingBlocks.Contracts;

// Every contract travels in MassTransit's envelope (MessageId, CorrelationId, ConversationId, SentTime,
// MessageType). Each payload also carries the business CorrelationId; the publish filter in
// Messaging/PayloadCorrelationIdFilter copies it into the envelope's CorrelationId.

/// <summary>A contract that carries the business correlation id of the request that started the flow.</summary>
public interface ICorrelatedEvent
{
    string CorrelationId { get; }
}

// Signal events — published by Ingestion
public sealed record ErrorSignalReceived(
    Guid SignalId,
    string SourceSystem,
    string ServiceName,
    string Environment,
    string? Region,
    string Fingerprint,
    string Message,
    string? TraceId,
    string? DeploymentId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string? IdempotencyKey = null
) : ICorrelatedEvent;

public sealed record LogSignalReceived(
    Guid SignalId,
    string SourceSystem,
    string ServiceName,
    string Environment,
    string? Region,
    string Severity,
    string Message,
    string? Fingerprint,
    string? TraceId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string? IdempotencyKey = null
) : ICorrelatedEvent;

public sealed record MetricSignalReceived(
    Guid SignalId,
    string SourceSystem,
    string ServiceName,
    string Environment,
    string? Region,
    string MetricName,
    double Value,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string? IdempotencyKey = null
) : ICorrelatedEvent;

public sealed record DeploymentSignalReceived(
    Guid SignalId,
    string SourceSystem,
    string ServiceName,
    string Environment,
    string? Region,
    string DeploymentId,
    string Version,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string? IdempotencyKey = null
) : ICorrelatedEvent;

public sealed record HealthCheckSignalReceived(
    Guid SignalId,
    string SourceSystem,
    string ServiceName,
    string Environment,
    string? Region,
    bool IsHealthy,
    string? Reason,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string? IdempotencyKey = null
) : ICorrelatedEvent;

// Incident lifecycle events — published by Incident.Api
public sealed record IncidentCreated(
    Guid IncidentId,
    string ServiceName,
    string Environment,
    string? Region,
    string? Fingerprint,
    string? DeploymentId,
    string Severity,
    string RuleName,
    string Summary,
    DateTimeOffset DetectedAt,
    string CorrelationId
) : ICorrelatedEvent;

public sealed record IncidentAcknowledged(
    Guid IncidentId,
    string ServiceName,
    string Environment,
    string AcknowledgedBy,
    DateTimeOffset AcknowledgedAt,
    string CorrelationId
) : ICorrelatedEvent;

public sealed record IncidentResolved(
    Guid IncidentId,
    string ServiceName,
    string Environment,
    string ResolvedBy,
    DateTimeOffset ResolvedAt,
    string CorrelationId
) : ICorrelatedEvent;

public sealed record IncidentClosed(
    Guid IncidentId,
    string ServiceName,
    string Environment,
    DateTimeOffset ClosedAt,
    string CorrelationId
) : ICorrelatedEvent;

// Correlation events — published by Correlation.Worker
public sealed record CorrelationFound(
    Guid CorrelationGroupId,
    string CorrelationType,     // "deployment" | "fingerprint"
    string CorrelationKey,
    string Environment,
    string? DeploymentId,
    string? Fingerprint,
    IReadOnlyList<Guid> IncidentIds,
    DateTimeOffset DetectedAt,
    string CorrelationId
) : ICorrelatedEvent;

// Anomaly events — published by Detection
public sealed record AnomalyDetected(
    Guid AnomalyId,
    string ServiceName,
    string Environment,
    string? Region,
    string? Fingerprint,
    string? DeploymentId,
    string Severity,
    string RuleName,
    string Summary,
    DateTimeOffset DetectedAt,
    IReadOnlyList<Guid> SignalIds,
    string CorrelationId
) : ICorrelatedEvent;
