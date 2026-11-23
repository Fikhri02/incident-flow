using IncidentFlow.BuildingBlocks.Contracts;
using IncidentFlow.Services.Notification.Worker.Notifications;
using MassTransit;

namespace IncidentFlow.Services.Notification.Worker.Consumers;

public sealed class IncidentCreatedConsumer(
    ILogger<IncidentCreatedConsumer> logger,
    IIncidentNotifier notifier,
    ISentNotifications sent)
    : IConsumer<IncidentCreated>
{
    public async Task Consume(ConsumeContext<IncidentCreated> context)
    {
        var msg = context.Message;

        var messageId = context.MessageId;
        if (messageId is not null)
        {
            switch (await sent.TryClaimAsync(messageId.Value))
            {
                case NotificationClaim.Sent:
                    logger.LogInformation("Skipped redelivered notification for incident {IncidentId} (message {MessageId})",
                        msg.IncidentId, messageId);
                    return;
                case NotificationClaim.InFlight:
                    // Not proof of a post: its holder may have died. Throw so the message is retried, not acked.
                    throw new NotificationInFlightException(messageId.Value);
            }
        }

        // Structured log makes this observable via OTel; Slack gets the same message.
        logger.LogWarning(
            "[NOTIFY] New {Severity} incident {IncidentId} — {Service}/{Env} — {Summary}",
            msg.Severity.ToUpperInvariant(), msg.IncidentId,
            msg.ServiceName, msg.Environment, msg.Summary);

        try
        {
            await notifier.NotifyAsync(msg, context.CancellationToken);
        }
        catch
        {
            // Not sent: release the claim so MassTransit's retry (or an _error replay) can post it.
            if (messageId is not null)
            {
                try
                {
                    await sent.ReleaseAsync(messageId.Value);
                }
                catch (Exception releaseError)
                {
                    // The claim expires on its own; the failed post is the error to report.
                    logger.LogWarning(releaseError, "Could not release the claim on message {MessageId}", messageId);
                }
            }
            throw;
        }

        if (messageId is null) return;
        try
        {
            await sent.MarkSentAsync(messageId.Value);
        }
        catch (Exception markError)
        {
            // Posted: the message must be acked. Throwing here would post again once the claim expires.
            logger.LogWarning(markError,
                "Posted incident {IncidentId} but could not mark message {MessageId} as sent; a redelivery after the claim expires would post again",
                msg.IncidentId, messageId);
        }
    }
}
