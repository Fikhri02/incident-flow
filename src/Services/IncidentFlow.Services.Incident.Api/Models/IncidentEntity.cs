namespace IncidentFlow.Services.Incident.Api.Models;

public sealed class IncidentEntity
{
    public Guid IncidentId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string? Fingerprint { get; set; }
    public string? DeploymentId { get; set; }
    public string Severity { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = IncidentStatus.Open;
    public DateTimeOffset DetectedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    // stored as JSON array string
    public string SignalIdsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class IncidentStatus
{
    public const string Open = "open";
    public const string Acknowledged = "acknowledged";
    public const string Resolved = "resolved";
    public const string Closed = "closed";
}
