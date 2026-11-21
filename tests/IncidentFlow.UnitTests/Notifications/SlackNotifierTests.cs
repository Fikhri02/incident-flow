using System.Net;
using System.Text.Json;
using FluentAssertions;
using IncidentFlow.BuildingBlocks.Contracts;
using IncidentFlow.Services.Notification.Worker.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IncidentFlow.UnitTests.Notifications;

public sealed class SlackNotifierTests
{
    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<(Uri Url, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!, await request.Content!.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(status);
        }
    }

    private static readonly IncidentCreated Incident = JsonSerializer.Deserialize<IncidentCreated>("""
        {
          "incidentId": "6f1c2c3a-0000-4000-8000-000000000001",
          "serviceName": "payment-api",
          "environment": "prod",
          "severity": "high",
          "ruleName": "error-spike",
          "summary": "Payment timeout repeated 5 times in 5 minutes"
        }
        """, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static SlackNotifier Notifier(RecordingHandler handler, string? webhook) =>
        new(new HttpClient(handler), Options.Create(new SlackOptions { WebhookUrl = webhook }), NullLogger<SlackNotifier>.Instance);

    [Fact]
    public async Task Posts_the_incident_to_the_webhook()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);

        await Notifier(handler, "https://hooks.slack.test/services/T/B/X").NotifyAsync(Incident, CancellationToken.None);

        var (url, body) = handler.Requests.Should().ContainSingle().Subject;
        url.Should().Be(new Uri("https://hooks.slack.test/services/T/B/X"));
        var text = JsonDocument.Parse(body).RootElement.GetProperty("text").GetString();
        text.Should().Contain("HIGH").And.Contain(Incident.IncidentId.ToString()).And.Contain("payment-api/prod");
    }

    [Fact]
    public async Task Without_a_webhook_it_only_logs()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);

        await Notifier(handler, webhook: null).NotifyAsync(Incident, CancellationToken.None);

        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_post_throws_so_the_message_is_retried()
    {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable);

        var act = () => Notifier(handler, "https://hooks.slack.test/x").NotifyAsync(Incident, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
