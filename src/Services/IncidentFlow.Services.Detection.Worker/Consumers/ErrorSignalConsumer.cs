using IncidentFlow.BuildingBlocks.Contracts;
using IncidentFlow.Services.Detection.Worker.Detection;
using MassTransit;

namespace IncidentFlow.Services.Detection.Worker.Consumers;

public sealed class ErrorSignalConsumer(SpikeDetector detector, IPublishEndpoint bus, ILogger<ErrorSignalConsumer> logger)
    : IConsumer<ErrorSignalReceived>
{
    public async Task Consume(ConsumeContext<ErrorSignalReceived> context)
    {
        var msg = context.Message;

        var result = await detector.RecordAsync(
            msg.ServiceName, msg.Environment, msg.Fingerprint, msg.OccurredAt, msg.SignalId);

        if (!result.IsSpike)
            return;

        logger.LogWarning("Spike detected: {Count} errors in 5 min for {Service}/{Env} fingerprint={Fingerprint}",
            result.Count, msg.ServiceName, msg.Environment, msg.Fingerprint);

        await bus.Publish(new AnomalyDetected(
            AnomalyId: Guid.NewGuid(),
            ServiceName: msg.ServiceName,
            Environment: msg.Environment,
            Region: msg.Region,
            Fingerprint: msg.Fingerprint,
            DeploymentId: msg.DeploymentId,
            Severity: "high",
            RuleName: "ErrorSpike",
            Summary: $"{result.Count} errors in 5 min for {msg.ServiceName} [{msg.Fingerprint}]",
            DetectedAt: DateTimeOffset.UtcNow,
            SignalIds: [msg.SignalId],
            CorrelationId: msg.CorrelationId
        ), context.CancellationToken);
    }
}
