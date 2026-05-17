using IncidentFlow.BuildingBlocks.Contracts;
using MassTransit;

namespace IncidentFlow.Services.Notification.Worker.Consumers;

public sealed class IncidentCreatedConsumer(ILogger<IncidentCreatedConsumer> logger)
    : IConsumer<IncidentCreated>
{
    public Task Consume(ConsumeContext<IncidentCreated> context)
    {
        var msg = context.Message;

        // In production this dispatches to PagerDuty / Slack / email.
        // Structured log makes this observable via OTel.
        logger.LogWarning(
            "[NOTIFY] New {Severity} incident {IncidentId} — {Service}/{Env} — {Summary}",
            msg.Severity.ToUpperInvariant(), msg.IncidentId,
            msg.ServiceName, msg.Environment, msg.Summary);

        return Task.CompletedTask;
    }
}
