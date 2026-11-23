namespace IncidentFlow.Services.Reporting.Api.Models;

/// <summary>
/// One incident as Reporting sees it. Every lifecycle event fills its own fields, so they can arrive in any
/// order: a row that has not had its IncidentCreated yet (<see cref="DetectedAt"/> is null) is kept but not
/// counted in the reports until it does.
/// </summary>
public sealed class IncidentReportRow
{
    public Guid IncidentId { get; set; }
    public string? ServiceName { get; set; }
    public string? Environment { get; set; }
    public string? Severity { get; set; }
    public DateTimeOffset? DetectedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Named by a CorrelationFound of type "deployment": a deployment regression.</summary>
    public bool DeploymentLinked { get; set; }
}
