namespace IncidentFlow.Services.Timeline.Api.Models;

public sealed class TimelineEntryEntity
{
    public Guid EntryId { get; set; }

    /// <summary>Database-assigned and increasing: the order Timeline recorded entries in, and the events cursor.</summary>
    public long Sequence { get; set; }

    public Guid? IncidentId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? Actor { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The consumed message as JSON (web defaults), so other services can replay it. Null on older entries.</summary>
    public string? Payload { get; set; }
}
