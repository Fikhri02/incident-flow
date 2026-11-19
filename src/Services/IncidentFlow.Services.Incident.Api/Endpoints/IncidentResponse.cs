using IncidentFlow.Services.Incident.Api.Domain;

namespace IncidentFlow.Services.Incident.Api.Endpoints;

/// <summary>The HTTP shape of an incident. Same fields and names the API has always returned.</summary>
public sealed record IncidentResponse(
    Guid IncidentId,
    string ServiceName,
    string Environment,
    string? Region,
    string? Fingerprint,
    string? DeploymentId,
    string Severity,
    string RuleName,
    string Summary,
    string Status,
    DateTimeOffset DetectedAt,
    DateTimeOffset? AcknowledgedAt,
    string? AcknowledgedBy,
    DateTimeOffset? ResolvedAt,
    string? ResolvedBy,
    DateTimeOffset? ClosedAt,
    string CorrelationId,
    string SignalIdsJson,
    DateTimeOffset CreatedAt)
{
    public static IncidentResponse From(Domain.Incident i) => new(
        i.IncidentId, i.ServiceName, i.Environment, i.Region, i.Fingerprint, i.DeploymentId,
        i.Severity, i.RuleName, i.Summary, i.Status.ToWire(), i.DetectedAt,
        i.AcknowledgedAt, i.AcknowledgedBy, i.ResolvedAt, i.ResolvedBy, i.ClosedAt,
        i.CorrelationId, i.SignalIdsJson, i.CreatedAt);
}
