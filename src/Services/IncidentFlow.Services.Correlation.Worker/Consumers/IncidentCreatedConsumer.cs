using IncidentFlow.BuildingBlocks.Contracts;
using IncidentFlow.Services.Correlation.Worker.Correlation;
using MassTransit;

namespace IncidentFlow.Services.Correlation.Worker.Consumers;

public sealed class IncidentCreatedConsumer(
    CorrelationEngine engine,
    IPublishEndpoint bus,
    ILogger<IncidentCreatedConsumer> logger)
    : IConsumer<IncidentCreated>
{
    public async Task Consume(ConsumeContext<IncidentCreated> context)
    {
        var msg = context.Message;
        var matches = await engine.RecordAsync(msg);

        foreach (var match in matches)
        {
            logger.LogWarning(
                "Correlation found [{Type}] key={Key} incidents={Count} env={Env}",
                match.Type, match.Key, match.IncidentIds.Count, msg.Environment);

            await bus.Publish(new CorrelationFound(
                CorrelationGroupId: Guid.NewGuid(),
                CorrelationType: match.Type,
                CorrelationKey: match.Key,
                Environment: msg.Environment,
                DeploymentId: match.DeploymentId,
                Fingerprint: match.Fingerprint,
                IncidentIds: match.IncidentIds,
                DetectedAt: DateTimeOffset.UtcNow,
                CorrelationId: msg.CorrelationId
            ), context.CancellationToken);
        }
    }
}
