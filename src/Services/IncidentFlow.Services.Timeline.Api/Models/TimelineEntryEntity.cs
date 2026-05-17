namespace IncidentFlow.Services.Timeline.Api.Models;

public sealed class TimelineEntryEntity
{
    public Guid EntryId { get; set; }
    public Guid? IncidentId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? Actor { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}
