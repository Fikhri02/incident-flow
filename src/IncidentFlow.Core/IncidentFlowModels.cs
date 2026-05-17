namespace IncidentFlow.Core;

public enum IncidentSeverity
{
    SEV1,
    SEV2,
    SEV3,
    SEV4
}

public enum IncidentStatus
{
    Detected,
    Open,
    Acknowledged,
    Investigating,
    Mitigating,
    Resolved,
    Closed,
    Reopened
}

public sealed record EventEnvelope(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    string CorrelationId,
    string? CausationId,
    string Source,
    DateTimeOffset OccurredAt,
    object Payload
);

public sealed record OutboxMessage(
    Guid Id,
    string EventType,
    int SchemaVersion,
    string Payload,
    string CorrelationId,
    string? CausationId,
    DateTimeOffset OccurredAt,
    DateTimeOffset? PublishedAt,
    int RetryCount,
    string? Error
);

public sealed record InboxMessage(
    Guid MessageId,
    string ConsumerName,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt,
    string Status,
    string? Error
);

public sealed record ErrorSignalRequest(
    string SourceSystem,
    string ServiceName,
    string Environment,
    string Message,
    string Fingerprint,
    DateTimeOffset? OccurredAt = null,
    string? Region = null,
    string? TraceId = null,
    string? DeploymentId = null,
    Dictionary<string, string>? Metadata = null,
    string? IdempotencyKey = null
);

public sealed record LogSignalRequest(
    string SourceSystem,
    string ServiceName,
    string Environment,
    string Severity,
    string Message,
    DateTimeOffset? OccurredAt = null,
    string? Region = null,
    string? TraceId = null,
    string? Fingerprint = null,
    string? DeploymentId = null,
    Dictionary<string, string>? Metadata = null,
    string? IdempotencyKey = null
);

public sealed record MetricSignalRequest(
    string SourceSystem,
    string ServiceName,
    string Environment,
    string MetricName,
    double Value,
    DateTimeOffset? OccurredAt = null,
    string? Region = null,
    Dictionary<string, string>? Metadata = null,
    string? IdempotencyKey = null
);

public sealed record TraceSignalRequest(
    string SourceSystem,
    string ServiceName,
    string Environment,
    string TraceId,
    string OperationName,
    double DurationMs,
    int StatusCode,
    DateTimeOffset? OccurredAt = null,
    string? Region = null,
    string? DeploymentId = null,
    Dictionary<string, string>? Metadata = null,
    string? IdempotencyKey = null
);

public sealed record HealthCheckSignalRequest(
    string SourceSystem,
    string ServiceName,
    string Environment,
    bool IsHealthy,
    string? Reason = null,
    DateTimeOffset? OccurredAt = null,
    string? Region = null,
    Dictionary<string, string>? Metadata = null,
    string? IdempotencyKey = null
);

public sealed record DeploymentSignalRequest(
    string SourceSystem,
    string ServiceName,
    string Environment,
    string DeploymentId,
    string Version,
    DateTimeOffset? OccurredAt = null,
    string? Region = null,
    Dictionary<string, string>? Metadata = null,
    string? IdempotencyKey = null
);

public sealed record UserReportSignalRequest(
    string SourceSystem,
    string ServiceName,
    string Environment,
    string Summary,
    DateTimeOffset? OccurredAt = null,
    string? Region = null,
    Dictionary<string, string>? Metadata = null,
    string? IdempotencyKey = null
);

public sealed record SignalRecord(
    Guid SignalId,
    string SignalType,
    string SourceSystem,
    string ServiceName,
    string Environment,
    string? Region,
    string? Fingerprint,
    string? TraceId,
    string? DeploymentId,
    DateTimeOffset OccurredAt,
    string Summary,
    Dictionary<string, string> Metadata,
    string? IdempotencyKey = null
);

public sealed record SignalAcceptedResponse(
    Guid SignalId,
    string CorrelationId,
    bool Duplicate,
    string Message
);

public sealed record AnomalyRecord(
    Guid AnomalyId,
    string ServiceName,
    string Environment,
    string? Region,
    string? Fingerprint,
    string? DeploymentId,
    IncidentSeverity Severity,
    string RuleName,
    string Summary,
    DateTimeOffset DetectedAt,
    IReadOnlyList<Guid> SignalIds
);

public sealed record IncidentRecord(
    Guid IncidentId,
    string Title,
    string Description,
    IncidentSeverity Severity,
    IncidentStatus Status,
    string AffectedService,
    string Environment,
    string? Region,
    string? OwnerTeamId,
    string? AssignedEngineerId,
    DateTimeOffset DetectedAt,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt,
    string? RootCauseSummary,
    string? ResolutionSummary,
    string? ReopenReason,
    IReadOnlyList<Guid> LinkedSignalIds
);

public sealed record TimelineEntry(
    Guid EntryId,
    Guid? IncidentId,
    string EntryType,
    string Message,
    DateTimeOffset OccurredAt,
    string CorrelationId
);

public sealed record NotificationRecord(
    Guid NotificationId,
    Guid IncidentId,
    string Channel,
    string Recipient,
    string Status,
    string Message,
    DateTimeOffset SentAt,
    int RetryCount = 0,
    DateTimeOffset? NextRetryAt = null
);

public sealed record RecommendationRecord(
    Guid RecommendationId,
    Guid IncidentId,
    string Category,
    string Title,
    string Detail,
    int Confidence,
    string Status,
    DateTimeOffset CreatedAt
);

public sealed record ActionItem(
    string Title,
    string Description,
    string Owner,
    DateTimeOffset? DueAt,
    string Status
);

public sealed record PostmortemRecord(
    Guid PostmortemId,
    Guid IncidentId,
    string Status,
    string Summary,
    string Impact,
    string Timeline,
    string RootCause,
    string Resolution,
    string WhatWentWell,
    string WhatWentWrong,
    IReadOnlyList<ActionItem> ActionItems,
    string LessonsLearned,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt = null
);

public sealed record IncidentSummaryReport(
    int TotalIncidents,
    int OpenIncidents,
    int ResolvedIncidents,
    double MeanTimeToAcknowledgeMinutes,
    double MeanTimeToResolveMinutes,
    IReadOnlyDictionary<string, int> SeverityBreakdown,
    IReadOnlyDictionary<string, int> TopFailingServices,
    IReadOnlyDictionary<string, int> DeploymentRegressionStats
);

public sealed record ReportFilter(string? ServiceName = null, string? Environment = null);

public sealed record AcknowledgeIncidentRequest(string EngineerId);

public sealed record AssignIncidentRequest(string EngineerId, string? TeamId = null);

public sealed record EscalateIncidentRequest(IncidentSeverity Severity, string Reason);

public sealed record ResolveIncidentRequest(string ResolutionSummary, string? RootCauseSummary = null);

public sealed record ReopenIncidentRequest(string Reason);

public sealed record UpdateRecommendationStatusRequest(string Status);

public sealed record UpdatePostmortemRequest(
    string? Status = null,
    string? Summary = null,
    string? Impact = null,
    string? RootCause = null,
    string? Resolution = null,
    string? WhatWentWell = null,
    string? WhatWentWrong = null,
    string? LessonsLearned = null
);

public sealed record EscalationSweepRequest(int AcknowledgementSlaMinutes = 15);

public sealed record RuntimeReadinessReport(
    string Runtime,
    string Gateway,
    string Messaging,
    string Cache,
    string Persistence,
    string Observability,
    string TestStrategy,
    IReadOnlyList<string> ExternalAdapterBacklog
);
