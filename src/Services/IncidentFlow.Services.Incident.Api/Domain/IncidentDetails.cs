namespace IncidentFlow.Services.Incident.Api.Domain;

/// <summary>What the detector knew when it raised the anomaly that opens an incident.</summary>
public sealed record IncidentDetails(
    string? Region,
    string? DeploymentId,
    string Severity,
    string RuleName,
    string Summary,
    DateTimeOffset DetectedAt,
    string CorrelationId,
    IReadOnlyList<Guid> SignalIds);
