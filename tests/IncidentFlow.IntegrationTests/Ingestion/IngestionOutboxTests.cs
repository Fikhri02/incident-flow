using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IncidentFlow.IntegrationTests.Shared;
using Xunit;

namespace IncidentFlow.IntegrationTests.Ingestion;

/// <summary>The Ingestion API with its real MassTransit event bus; the bus is never started.</summary>
public sealed class IngestionOutboxApiFactory : IngestionApiFactory
{
    protected override bool ReplaceEventBus => false;
}

// A signal and its integration event are committed together: with no broker running, the event
// waits in OutboxMessage next to the saved signal, and a failed save leaves neither behind.
public sealed class IngestionOutboxTests(IngestionOutboxApiFactory factory) : IClassFixture<IngestionOutboxApiFactory>
{
    private readonly HttpClient _client = CreateClient(factory);

    private static HttpClient CreateClient(IngestionOutboxApiFactory factory)
    {
        var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    private static object Error(string serviceName = "payment-api") => new
    {
        sourceSystem = "test",
        serviceName,
        environment = "prod",
        message = "Timeout",
        fingerprint = "payment-timeout"
    };

    [Fact]
    public async Task PostError_WritesTheEventToTheOutbox_WithTheSignal()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/signals/errors", Error());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var signalId = (await response.Content.ReadFromJsonAsync<JsonDocument>())!
            .RootElement.GetProperty("signalId").GetGuid();
        var rows = await OutboxRows.WhereBodyContainsAsync(factory.ConnectionString, signalId.ToString());
        rows.Should().ContainSingle().Which.MessageType.Should().Contain("ErrorSignalReceived");
        (await OutboxRows.CountAsync(factory.ConnectionString,
            """SELECT count(*) FROM "Signals" WHERE "SignalId" = @id""", ("id", signalId))).Should().Be(1);
    }

    [Fact]
    public async Task FailedSave_LeavesNoOutboxMessage()
    {
        // ServiceName is limited to 100 characters, so the insert fails after the event was published.
        var tooLong = $"svc-{Guid.NewGuid():N}-{new string('x', 120)}";

        var response = await _client.PostAsJsonAsync("/api/v1/signals/errors", Error(tooLong));

        response.IsSuccessStatusCode.Should().BeFalse();
        (await OutboxRows.WhereBodyContainsAsync(factory.ConnectionString, tooLong)).Should().BeEmpty();
    }

    [Fact]
    public async Task PublishedEvent_CarriesTheRequestCorrelationIdInTheEnvelope()
    {
        var correlationId = Guid.NewGuid();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/signals/errors")
        {
            Content = JsonContent.Create(Error())
        };
        request.Headers.Add("x-correlation-id", correlationId.ToString("N"));

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var signalId = (await response.Content.ReadFromJsonAsync<JsonDocument>())!
            .RootElement.GetProperty("signalId").GetGuid();
        var row = (await OutboxRows.WhereBodyContainsAsync(factory.ConnectionString, signalId.ToString())).Single();
        row.CorrelationId.Should().Be(correlationId);
        row.Body.Should().Contain(correlationId.ToString("N"));
    }
}
