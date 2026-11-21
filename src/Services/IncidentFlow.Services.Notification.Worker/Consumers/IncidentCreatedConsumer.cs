using IncidentFlow.BuildingBlocks.Contracts;
using IncidentFlow.Services.Notification.Worker.Notifications;
using MassTransit;

namespace IncidentFlow.Services.Notification.Worker.Consumers;

public sealed class IncidentCreatedConsumer(ILogger<IncidentCreatedConsumer> logger, IIncidentNotifier notifier)
    : IConsumer<IncidentCreated>
{
    public async Task Consume(ConsumeContext<IncidentCreated> context)
    {
        var msg = context.Message;

        // Structured log makes this observable via OTel; Slack gets the same message.
        logger.LogWarning(
            "[NOTIFY] New {Severity} incident {IncidentId} — {Service}/{Env} — {Summary}",
            msg.Severity.ToUpperInvariant(), msg.IncidentId,
            msg.ServiceName, msg.Environment, msg.Summary);

        await notifier.NotifyAsync(msg, context.CancellationToken);
    }
}
