using System.Text.Json;
using IncidentFlow.BuildingBlocks.Contracts;
using IncidentFlow.BuildingBlocks.EventBus;
using IncidentFlow.Services.Incident.Api.Data;
using IncidentFlow.Services.Incident.Api.Models;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Services.Incident.Api.Consumers;

public sealed class AnomalyDetectedConsumer(IncidentDbContext db, IEventBus bus, ILogger<AnomalyDetectedConsumer> logger)
    : IConsumer<AnomalyDetected>
{
    public async Task Consume(ConsumeContext<AnomalyDetected> context)
    {
        var msg = context.Message;

        var existing = await db.Incidents.FirstOrDefaultAsync(i =>
            i.ServiceName == msg.ServiceName &&
            i.Environment == msg.Environment &&
            i.Fingerprint == msg.Fingerprint &&
            (i.Status == IncidentStatus.Open || i.Status == IncidentStatus.Acknowledged),
            context.CancellationToken);

        if (existing is not null)
        {
            logger.LogDebug("Suppressed duplicate incident for {Service}/{Env} fingerprint={Fingerprint}",
                msg.ServiceName, msg.Environment, msg.Fingerprint);
            return;
        }

        var incident = new IncidentEntity
        {
            IncidentId = Guid.NewGuid(),
            ServiceName = msg.ServiceName,
            Environment = msg.Environment,
            Region = msg.Region,
            Fingerprint = msg.Fingerprint,
            DeploymentId = msg.DeploymentId,
            Severity = msg.Severity,
            RuleName = msg.RuleName,
            Summary = msg.Summary,
            Status = IncidentStatus.Open,
            DetectedAt = msg.DetectedAt,
            CorrelationId = msg.CorrelationId,
            SignalIdsJson = JsonSerializer.Serialize(msg.SignalIds),
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Incidents.Add(incident);
        await db.SaveChangesAsync(context.CancellationToken);

        await bus.PublishAsync(new IncidentCreated(
            incident.IncidentId, incident.ServiceName, incident.Environment,
            incident.Region, incident.Fingerprint, incident.DeploymentId,
            incident.Severity, incident.RuleName, incident.Summary,
            incident.DetectedAt, incident.CorrelationId),
            context.CancellationToken);

        logger.LogInformation("Incident {IncidentId} created for {Service}/{Env} [{Severity}]",
            incident.IncidentId, incident.ServiceName, incident.Environment, incident.Severity);
    }
}
