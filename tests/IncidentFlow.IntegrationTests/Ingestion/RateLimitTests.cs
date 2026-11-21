using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace IncidentFlow.IntegrationTests.Ingestion;

/// <summary>Ingestion with a limit of 3 per minute, so the window fills in a few requests.</summary>
public sealed class LowLimitIngestionApiFactory : IngestionApiFactory
{
    protected override int RateLimitPerMinute => 3;
}

public sealed class RateLimitTests(LowLimitIngestionApiFactory factory) : IClassFixture<LowLimitIngestionApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private Task<HttpResponseMessage> PostError(string source, string service) =>
        _client.PostAsJsonAsync("/api/v1/signals/errors", new
        {
            sourceSystem = source,
            serviceName = service,
            environment = "prod",
            message = "Payment timeout",
            fingerprint = "payment-timeout"
        });

    [Fact]
    public async Task A_source_over_its_limit_gets_429_with_Retry_After()
    {
        var source = $"noisy-{Guid.NewGuid():N}";
        for (var i = 0; i < 3; i++)
            (await PostError(source, "payment-api")).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var rejected = await PostError(source, "payment-api");

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.Should().NotBeNull();
        rejected.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Each_source_has_its_own_window()
    {
        var source = $"noisy-{Guid.NewGuid():N}";
        for (var i = 0; i < 4; i++) await PostError(source, "payment-api");

        var otherService = await PostError(source, "checkout-api");
        var otherSystem = await PostError($"quiet-{Guid.NewGuid():N}", "payment-api");

        otherService.StatusCode.Should().Be(HttpStatusCode.Accepted);
        otherSystem.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Source_names_differing_only_in_case_share_a_window()
    {
        var source = $"Noisy-{Guid.NewGuid():N}";
        for (var i = 0; i < 3; i++) await PostError(source, "payment-api");

        var rejected = await PostError(source.ToUpperInvariant(), "PAYMENT-API");

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
