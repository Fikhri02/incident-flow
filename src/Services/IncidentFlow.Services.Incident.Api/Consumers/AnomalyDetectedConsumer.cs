using IncidentFlow.BuildingBlocks.Contracts;
using IncidentFlow.Services.Incident.Api.Domain;
using IncidentFlow.Services.Incident.Api.Messaging;
using MassTransit;

namespace IncidentFlow.Services.Incident.Api.Consumers;

public sealed class AnomalyDetectedConsumer(
    IIncidentRepository incidents,
    IncidentEventPublisher events,
    TimeProvider clock,
    ILogger<AnomalyDetectedConsumer> logger)
    : IConsumer<AnomalyDetected>
{
    public async Task Consume(ConsumeContext<AnomalyDetected> context)
    {
        var msg = context.Message;
        var key = new IncidentKey(msg.ServiceName, msg.Environment, msg.Fingerprint);

        if (await incidents.FindActiveAsync(key, context.CancellationToken) is not null)
        {
            logger.LogDebug("Suppressed duplicate incident for {Service}/{Env} fingerprint={Fingerprint}",
                msg.ServiceName, msg.Environment, msg.Fingerprint);
            return;
        }

        var incident = Domain.Incident.Open(key, new IncidentDetails(
            msg.Region, msg.DeploymentId, msg.Severity, msg.RuleName, msg.Summary,
            msg.DetectedAt, msg.CorrelationId, msg.SignalIds), clock.GetUtcNow());

        incidents.Add(incident);
        await incidents.SaveChangesAsync(context.CancellationToken);
        await events.PublishAsync(incident, context.CancellationToken);

        logger.LogInformation("Incident {IncidentId} created for {Service}/{Env} [{Severity}]",
            incident.IncidentId, incident.ServiceName, incident.Environment, incident.Severity);
    }
}
