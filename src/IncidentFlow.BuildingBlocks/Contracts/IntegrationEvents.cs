namespace IncidentFlow.BuildingBlocks.Contracts;

public sealed record IntegrationEvent(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    string CorrelationId,
    string? CausationId,
    string Source,
    DateTimeOffset OccurredAt
);

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
);

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
);

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
);

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
);

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
);

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
);

public sealed record IncidentAcknowledged(
    Guid IncidentId,
    string ServiceName,
    string Environment,
    string AcknowledgedBy,
    DateTimeOffset AcknowledgedAt,
    string CorrelationId
);

public sealed record IncidentResolved(
    Guid IncidentId,
    string ServiceName,
    string Environment,
    string ResolvedBy,
    DateTimeOffset ResolvedAt,
    string CorrelationId
);

public sealed record IncidentClosed(
    Guid IncidentId,
    string ServiceName,
    string Environment,
    DateTimeOffset ClosedAt,
    string CorrelationId
);

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
);

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
);
