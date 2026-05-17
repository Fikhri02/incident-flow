using System.Text.Json;

namespace IncidentFlow.Services.Ingestion.Api.Models;

public sealed class SignalEntity
{
    public Guid SignalId { get; set; }
    public string SignalType { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string? Fingerprint { get; set; }
    public string? TraceId { get; set; }
    public string? DeploymentId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
    public string? IdempotencyKey { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}
