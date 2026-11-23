using FluentAssertions;
using IncidentFlow.Services.Ingestion.Api.Endpoints;
using IncidentFlow.Services.Ingestion.Api.RateLimiting;
using Microsoft.Extensions.Configuration;
using Reqnroll;

namespace IncidentFlow.Specs.Steps;

/// <summary>
/// Drives the real <see cref="SignalRateLimiter"/> (Ingestion.Api) with the request record the
/// errors endpoint receives. The endpoint filter turns a refusal into 429 with Retry-After set to
/// the limiter's retry time in whole seconds; IntegrationTests/Ingestion/RateLimitTests checks that HTTP edge.
/// </summary>
[Binding]
[Scope(Feature = "Ingestion rate limit per source")]
public sealed class IngestionRateLimitSteps : IDisposable
{
    private SignalRateLimiter _limiter = null!;
    private readonly List<TimeSpan> _refusals = [];
    private int _accepted;

    [Given("Ingestion runs with the default rate limit")]
    public void GivenTheDefaultLimit()
    {
        // No Ingestion:RateLimit:PermitsPerMinute setting, as in a default deployment.
        var configuration = new ConfigurationBuilder().Build();
        _limiter = new SignalRateLimiter(configuration);
    }

    [Then("each source may send {int} signals a minute")]
    public void ThenEachSourceMaySend(int permits) =>
        _limiter.PermitsPerMinute.Should().Be(permits);

    [Given("{string} has used all its signals for {string} in {string} this minute")]
    public void GivenHasUsedAllItsSignals(string system, string service, string environment)
    {
        Send(system, service, environment, _limiter.PermitsPerMinute + 1);
        _refusals.Should().HaveCount(1, "the source must be over its limit before the scenario's action");
        _accepted = 0;
        _refusals.Clear();
    }

    [When("{string} sends {int} signals for {string} in {string} within a minute")]
    [When("{string} sends {int} signal for {string} in {string}")]
    public void WhenSends(string system, int count, string service, string environment) =>
        Send(system, service, environment, count);

    [Then("{int} signals are accepted")]
    [Then("{int} signal is accepted")]
    public void ThenAccepted(int count) => _accepted.Should().Be(count);

    [Then("{int} signals are refused")]
    [Then("{int} signal is refused")]
    public void ThenRefused(int count) => _refusals.Should().HaveCount(count);

    [Then("the refusal tells the sender to retry within {int} minute")]
    public void ThenRetryWithin(int minutes)
    {
        var retryAfter = _refusals.Single();
        retryAfter.Should().BeGreaterThan(TimeSpan.Zero);
        retryAfter.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(minutes));
    }

    private void Send(string system, string service, string environment, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var signal = new ErrorSignalRequest(system, service, environment, "Payment timeout", "payment-timeout");
            if (_limiter.TryAcquire(signal, out var retryAfter))
                _accepted++;
            else
                _refusals.Add(retryAfter);
        }
    }

    public void Dispose() => _limiter?.Dispose();
}
