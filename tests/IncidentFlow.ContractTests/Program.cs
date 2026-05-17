using System.Text.Json;
using System.Text.Json.Serialization;
using IncidentFlow.Core;

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter() }
};

var engine = new IncidentFlowEngine();
var correlationId = Guid.NewGuid().ToString("N");

engine.IngestDeployment(new DeploymentSignalRequest(
    "deploy-bot",
    "Payment API",
    "production",
    "deploy-1.8.2",
    "1.8.2",
    IdempotencyKey: "contract-deployment"
), correlationId);

for (var index = 0; index < 5; index++)
{
    engine.IngestError(new ErrorSignalRequest(
        "checkout-web",
        "Payment API",
        "production",
        "Timeout while authorizing payment",
        "payment-timeout",
        TraceId: $"trace-{index}",
        DeploymentId: "deploy-1.8.2",
        IdempotencyKey: $"contract-error-{index}"
    ), correlationId);
}

engine.IngestMetric(new MetricSignalRequest(
    "checkout-web",
    "Checkout API",
    "production",
    "p95_latency_ms",
    2400,
    IdempotencyKey: "contract-metric"
), correlationId);

engine.IngestLog(new LogSignalRequest(
    "worker",
    "Worker API",
    "production",
    "error",
    "Downstream dependency timeout",
    Fingerprint: "worker-timeout",
    IdempotencyKey: "contract-log"
), correlationId);

engine.IngestTrace(new TraceSignalRequest(
    "gateway",
    "Gateway API",
    "production",
    "trace-contract",
    "GET /status",
    120,
    200,
    IdempotencyKey: "contract-trace"
), correlationId);

var incident = engine.GetIncidents().First();
engine.AcknowledgeIncident(incident.IncidentId, new AcknowledgeIncidentRequest("irfan"), correlationId);
engine.ResolveIncident(incident.IncidentId, new ResolveIncidentRequest(
    "Rollback completed.",
    "Deployment deploy-1.8.2 introduced the regression."
), correlationId);
engine.UpdatePostmortem(incident.IncidentId, new UpdatePostmortemRequest(Status: "Reviewed"), correlationId);

var events = engine.GetEvents();
Assert(events.Count > 0, "events should be published");
foreach (var envelope in events)
{
    Assert(envelope.EventId != Guid.Empty, $"{envelope.EventType} must include eventId");
    Assert(!string.IsNullOrWhiteSpace(envelope.EventType), "eventType is required");
    Assert(envelope.SchemaVersion == 1, $"{envelope.EventType} schema version should be v1");
    Assert(!string.IsNullOrWhiteSpace(envelope.CorrelationId), $"{envelope.EventType} must include correlationId");
    Assert(!string.IsNullOrWhiteSpace(envelope.Source), $"{envelope.EventType} must include source");
    Assert(envelope.OccurredAt.Offset == TimeSpan.Zero, $"{envelope.EventType} must use UTC timestamp");
    Assert(envelope.Payload is not null, $"{envelope.EventType} must include payload");

    var serialized = JsonSerializer.Serialize(envelope, jsonOptions);
    var deserialized = JsonSerializer.Deserialize<EventEnvelope>(serialized, jsonOptions);
    Assert(deserialized is not null, $"{envelope.EventType} should deserialize");
    Assert(deserialized!.EventType == envelope.EventType, $"{envelope.EventType} should round-trip eventType");
}

var requiredEvents = new[]
{
    "DeploymentSignalReceived",
    "ErrorSignalReceived",
    "MetricSignalReceived",
    "LogSignalReceived",
    "TraceSignalReceived",
    "AnomalyDetected",
    "SignalsCorrelated",
    "IncidentCandidateDetected",
    "IncidentCreated",
    "NotificationSent",
    "RecommendationGenerated",
    "IncidentAcknowledged",
    "IncidentResolved",
    "PostmortemCreated",
    "PostmortemUpdated",
    "ReportingProjectionUpdated"
};

foreach (var requiredEvent in requiredEvents)
{
    Assert(events.Any(e => e.EventType == requiredEvent), $"{requiredEvent} should be emitted");
}

var outbox = engine.GetOutboxMessages();
Assert(outbox.Count == events.Count, "outbox should mirror published event count in local adapter");
Assert(outbox.All(m => m.PublishedAt.HasValue), "local outbox messages should be marked published");
Assert(engine.GetInboxMessages().Any(m => m.Status == "Processed"), "inbox should track idempotent consumer processing");

Console.WriteLine("IncidentFlow contract checks passed.");
Console.WriteLine($"Events checked: {events.Count}");
Console.WriteLine($"Outbox messages: {outbox.Count}");

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
