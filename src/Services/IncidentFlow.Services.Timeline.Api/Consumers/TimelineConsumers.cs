using IncidentFlow.BuildingBlocks.Contracts;
using IncidentFlow.Services.Timeline.Api.Data;
using IncidentFlow.Services.Timeline.Api.Models;
using MassTransit;

namespace IncidentFlow.Services.Timeline.Api.Consumers;

public sealed class IncidentCreatedConsumer(TimelineDbContext db) : IConsumer<IncidentCreated>
{
    public Task Consume(ConsumeContext<IncidentCreated> context)
    {
        var msg = context.Message;
        return TimelineAppend.Append(db, msg.IncidentId, "incident.created", msg.ServiceName, msg.Environment,
            $"Incident opened — {msg.Summary} [{msg.Severity}]", msg.CorrelationId, null,
            msg.DetectedAt, context.CancellationToken);
    }
}

public sealed class IncidentAcknowledgedConsumer(TimelineDbContext db) : IConsumer<IncidentAcknowledged>
{
    public Task Consume(ConsumeContext<IncidentAcknowledged> context)
    {
        var msg = context.Message;
        return TimelineAppend.Append(db, msg.IncidentId, "incident.acknowledged", msg.ServiceName, msg.Environment,
            $"Incident acknowledged by {msg.AcknowledgedBy}", msg.CorrelationId, msg.AcknowledgedBy,
            msg.AcknowledgedAt, context.CancellationToken);
    }
}

public sealed class IncidentResolvedConsumer(TimelineDbContext db) : IConsumer<IncidentResolved>
{
    public Task Consume(ConsumeContext<IncidentResolved> context)
    {
        var msg = context.Message;
        return TimelineAppend.Append(db, msg.IncidentId, "incident.resolved", msg.ServiceName, msg.Environment,
            $"Incident resolved by {msg.ResolvedBy}", msg.CorrelationId, msg.ResolvedBy,
            msg.ResolvedAt, context.CancellationToken);
    }
}

public sealed class IncidentClosedConsumer(TimelineDbContext db) : IConsumer<IncidentClosed>
{
    public Task Consume(ConsumeContext<IncidentClosed> context)
    {
        var msg = context.Message;
        return TimelineAppend.Append(db, msg.IncidentId, "incident.closed", msg.ServiceName, msg.Environment,
            "Incident closed", msg.CorrelationId, null, msg.ClosedAt, context.CancellationToken);
    }
}

public sealed class AnomalyDetectedConsumer(TimelineDbContext db) : IConsumer<AnomalyDetected>
{
    public Task Consume(ConsumeContext<AnomalyDetected> context)
    {
        var msg = context.Message;
        return TimelineAppend.Append(db, null, "anomaly.detected", msg.ServiceName, msg.Environment,
            $"Anomaly detected — {msg.Summary} (rule: {msg.RuleName})", msg.CorrelationId, null,
            msg.DetectedAt, context.CancellationToken);
    }
}

public sealed class ErrorSignalConsumer(TimelineDbContext db) : IConsumer<ErrorSignalReceived>
{
    public Task Consume(ConsumeContext<ErrorSignalReceived> context)
    {
        var msg = context.Message;
        return TimelineAppend.Append(db, null, "signal.error", msg.ServiceName, msg.Environment,
            $"Error: {msg.Message}", msg.CorrelationId, null, msg.OccurredAt, context.CancellationToken);
    }
}

file static class TimelineAppend
{
    internal static async Task Append(
        TimelineDbContext db,
        Guid? incidentId,
        string eventType,
        string serviceName,
        string environment,
        string summary,
        string correlationId,
        string? actor,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        db.Entries.Add(new TimelineEntryEntity
        {
            EntryId = Guid.NewGuid(),
            IncidentId = incidentId,
            EventType = eventType,
            ServiceName = serviceName,
            Environment = environment,
            Summary = summary,
            CorrelationId = correlationId,
            Actor = actor,
            OccurredAt = occurredAt
        });
        await db.SaveChangesAsync(ct);
    }
}
