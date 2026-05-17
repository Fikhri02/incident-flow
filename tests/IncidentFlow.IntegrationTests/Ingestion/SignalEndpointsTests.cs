using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace IncidentFlow.IntegrationTests.Ingestion;

public sealed class SignalEndpointsTests(IngestionApiFactory factory) : IClassFixture<IngestionApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task PostError_Returns202_WithSignalId()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/signals/errors", new
        {
            sourceSystem = "test-system",
            serviceName = "payment-api",
            environment = "prod",
            message = "Payment timeout",
            fingerprint = "payment-timeout"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        body!.RootElement.GetProperty("signalId").GetGuid().Should().NotBeEmpty();
        body.RootElement.GetProperty("duplicate").GetBoolean().Should().BeFalse();
        body.RootElement.GetProperty("message").GetString().Should().Be("Signal accepted.");
    }

    [Fact]
    public async Task PostError_WithIdempotencyKey_SecondCallIsSuppressed()
    {
        var idempotencyKey = $"idem-{Guid.NewGuid()}";
        var payload = new
        {
            sourceSystem = "test-system",
            serviceName = "checkout-api",
            environment = "prod",
            message = "DB timeout",
            fingerprint = "checkout-db-timeout",
            idempotencyKey
        };

        var r1 = await _client.PostAsJsonAsync("/api/v1/signals/errors", payload);
        var r2 = await _client.PostAsJsonAsync("/api/v1/signals/errors", payload);

        r1.StatusCode.Should().Be(HttpStatusCode.Accepted);
        r2.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var b1 = await r1.Content.ReadFromJsonAsync<JsonDocument>();
        var b2 = await r2.Content.ReadFromJsonAsync<JsonDocument>();

        b1!.RootElement.GetProperty("duplicate").GetBoolean().Should().BeFalse();
        b2!.RootElement.GetProperty("duplicate").GetBoolean().Should().BeTrue();
        // Suppressed signals return the original signalId
        b2.RootElement.GetProperty("signalId").GetGuid()
            .Should().Be(b1.RootElement.GetProperty("signalId").GetGuid());
    }

    [Fact]
    public async Task PostMetric_Returns202()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/signals/metrics", new
        {
            sourceSystem = "metrics-agent",
            serviceName = "auth-api",
            environment = "prod",
            metricName = "p95_latency_ms",
            value = 1850.0
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task PostDeployment_Returns202()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/signals/deployments", new
        {
            sourceSystem = "deploy-bot",
            serviceName = "catalog-api",
            environment = "prod",
            deploymentId = "deploy-2.4.0",
            version = "2.4.0"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task PostHealthCheck_Returns202()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/signals/health-checks", new
        {
            sourceSystem = "health-agent",
            serviceName = "auth-api",
            environment = "prod",
            isHealthy = false,
            reason = "Token issuer unavailable"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task GetSignals_ReturnsPersistedSignals()
    {
        // Seed a signal
        await _client.PostAsJsonAsync("/api/v1/signals/errors", new
        {
            sourceSystem = "test-system",
            serviceName = "inventory-api",
            environment = "staging",
            message = "Queue overflow",
            fingerprint = "queue-overflow"
        });

        var response = await _client.GetAsync("/api/v1/signals/");
        response.EnsureSuccessStatusCode();

        var signals = await response.Content.ReadFromJsonAsync<JsonDocument>();
        signals!.RootElement.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CorrelationId_PropagatedInResponseHeader()
    {
        var knownId = Guid.NewGuid().ToString("N");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/signals/errors")
        {
            Content = JsonContent.Create(new
            {
                sourceSystem = "test",
                serviceName = "payment-api",
                environment = "prod",
                message = "Timeout",
                fingerprint = "payment-timeout"
            })
        };
        request.Headers.Add("x-correlation-id", knownId);

        var response = await _client.SendAsync(request);

        response.Headers.TryGetValues("x-correlation-id", out var values).Should().BeTrue();
        values!.First().Should().Be(knownId);
    }
}
