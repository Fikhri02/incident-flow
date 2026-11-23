using System.Globalization;
using FluentAssertions;
using IncidentFlow.Services.Detection.Worker.Detection;
using Reqnroll;
using StackExchange.Redis;

namespace IncidentFlow.Specs.Steps;

/// <summary>Drives the real <see cref="SpikeDetector"/> (Detection.Worker) against Testcontainers Redis.</summary>
[Binding]
[Scope(Feature = "Error spike detection")]
public sealed class DetectionThresholdSteps(FeatureContext feature)
{
    private readonly SpikeDetector _detector = new(feature.FeatureContainer.Resolve<IConnectionMultiplexer>());

    // Scenarios share one Redis, so each scenario gets its own service name (part of the window key).
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];
    private readonly DateTimeOffset _first = DateTimeOffset.UtcNow;
    private readonly List<(double Minute, SpikeResult Result)> _results = [];
    private readonly Dictionary<string, SpikeResult> _lastByFingerprint = [];

    private string _service = string.Empty;
    private string _environment = string.Empty;
    private string _fingerprint = string.Empty;

    [Given("the detection window for {string} in {string} with fingerprint {string} is empty")]
    public async Task GivenTheWindowIsEmpty(string service, string environment, string fingerprint)
    {
        _service = $"{service}-{_run}";
        _environment = environment;
        _fingerprint = fingerprint;

        var key = $"spike:{_service}:{_environment}:{_fingerprint}";
        var db = feature.FeatureContainer.Resolve<IConnectionMultiplexer>().GetDatabase();
        (await db.KeyExistsAsync(key)).Should().BeFalse();
    }

    [When("errors arrive at these minutes after the first one:")]
    public async Task WhenErrorsArriveAt(DataTable table)
    {
        foreach (var row in table.Rows)
        {
            var minute = double.Parse(row["minute"], CultureInfo.InvariantCulture);
            await Record(_fingerprint, minute);
        }
    }

    [Given("{int} errors with fingerprint {string} arrived in the last minute")]
    public async Task GivenErrorsArrived(int count, string fingerprint)
    {
        for (var i = 0; i < count; i++)
            await Record(fingerprint, i * 0.1);
    }

    [When("{int} error with fingerprint {string} arrives")]
    public async Task WhenOneErrorArrives(int count, string fingerprint)
    {
        for (var i = 0; i < count; i++)
            await Record(fingerprint, 0.5 + i * 0.1);
    }

    [Then("exactly {int} anomaly is raised")]
    public void ThenExactlyAnomaliesAreRaised(int count) =>
        _results.Count(r => r.Result.IsSpike).Should().Be(count);

    [Then("no anomaly is raised")]
    public void ThenNoAnomalyIsRaised() =>
        _results.Should().NotContain(r => r.Result.IsSpike);

    [Then("the anomaly is raised by the error at minute {double}")]
    public void ThenTheAnomalyIsRaisedAt(double minute)
    {
        var fired = _results.Single(r => r.Result.IsSpike);
        fired.Minute.Should().Be(minute);
        fired.Result.Count.Should().Be(5);
    }

    [Then("the window holds {int} error")]
    [Then("the window holds {int} errors")]
    public void ThenTheWindowHolds(int count) =>
        _lastByFingerprint[_fingerprint].Count.Should().Be(count);

    [Then("the window for {string} holds {int} error")]
    public void ThenTheWindowForHolds(string fingerprint, int count) =>
        _lastByFingerprint[fingerprint].Count.Should().Be(count);

    private async Task Record(string fingerprint, double minute)
    {
        var result = await _detector.RecordAsync(
            _service, _environment, fingerprint, _first.AddMinutes(minute), Guid.NewGuid());
        _results.Add((minute, result));
        _lastByFingerprint[fingerprint] = result;
    }
}
