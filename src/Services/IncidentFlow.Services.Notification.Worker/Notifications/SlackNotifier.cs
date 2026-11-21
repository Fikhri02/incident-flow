using System.Net.Http.Json;
using IncidentFlow.BuildingBlocks.Contracts;
using Microsoft.Extensions.Options;

namespace IncidentFlow.Services.Notification.Worker.Notifications;

/// <summary>Bound from "Notifications:Slack".</summary>
public sealed class SlackOptions
{
    public const string SectionName = "Notifications:Slack";

    /// <summary>An incoming-webhook URL. Leave empty to only log notifications.</summary>
    public string? WebhookUrl { get; set; }
}

public interface IIncidentNotifier
{
    Task NotifyAsync(IncidentCreated incident, CancellationToken cancellationToken);
}

/// <summary>
/// Posts each new incident to a Slack incoming webhook. The HttpClient comes from
/// AddServiceDefaults, so every call goes through the standard resilience handler:
/// retries, timeouts and a circuit breaker that fails fast while Slack is down.
/// A call that still fails throws, so MassTransit retries the message and finally
/// moves it to notification-incident-created_error, where it can be redelivered.
/// </summary>
public sealed class SlackNotifier(HttpClient http, IOptions<SlackOptions> options, ILogger<SlackNotifier> logger)
    : IIncidentNotifier
{
    public async Task NotifyAsync(IncidentCreated incident, CancellationToken cancellationToken)
    {
        var webhook = options.Value.WebhookUrl;
        if (string.IsNullOrWhiteSpace(webhook))
        {
            logger.LogInformation("Slack webhook not configured; incident {IncidentId} was only logged", incident.IncidentId);
            return;
        }

        var text = $"New {incident.Severity.ToUpperInvariant()} incident {incident.IncidentId}: " +
                   $"{incident.ServiceName}/{incident.Environment}, {incident.Summary}";

        using var response = await http.PostAsJsonAsync(webhook, new { text }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
