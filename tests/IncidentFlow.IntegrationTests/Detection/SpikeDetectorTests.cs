using FluentAssertions;
using IncidentFlow.Services.Detection.Worker.Detection;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace IncidentFlow.IntegrationTests.Detection;

public sealed class SpikeDetectorTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder().Build();
    private SpikeDetector _detector = null!;
    private IConnectionMultiplexer _mux = null!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        var mux = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
        _mux = mux;
        _detector = new SpikeDetector(mux);
    }

    public async Task DisposeAsync() => await _redis.DisposeAsync();

    [Fact]
    public async Task FourErrors_DoNotTriggerSpike()
    {
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 4; i++)
        {
            var r = await _detector.RecordAsync("svc", "prod", "fp-no-spike", now.AddSeconds(i), Guid.NewGuid());
            r.IsSpike.Should().BeFalse();
        }
    }

    [Fact]
    public async Task FifthError_TriggersSpike_WithCorrectCount()
    {
        var now = DateTimeOffset.UtcNow;
        SpikeResult? last = null;
        for (var i = 0; i < 5; i++)
            last = await _detector.RecordAsync("svc", "prod", "fp-spike", now.AddSeconds(i), Guid.NewGuid());

        last!.IsSpike.Should().BeTrue();
        last.Count.Should().Be(5);
    }

    [Fact]
    public async Task AfterSpike_WindowResets_NextErrorDoesNotSpike()
    {
        var now = DateTimeOffset.UtcNow;
        // Trigger first spike
        for (var i = 0; i < 5; i++)
            await _detector.RecordAsync("svc", "prod", "fp-reset", now.AddSeconds(i), Guid.NewGuid());

        // One more error after the reset — window is empty, so no spike
        var r = await _detector.RecordAsync("svc", "prod", "fp-reset", now.AddSeconds(10), Guid.NewGuid());
        r.IsSpike.Should().BeFalse();
        r.Count.Should().Be(1);
    }

    [Fact]
    public async Task ErrorsOutsideWindow_AreNotCounted()
    {
        var now = DateTimeOffset.UtcNow;
        // 4 errors that are 10 minutes old — outside the 5-minute window
        for (var i = 0; i < 4; i++)
            await _detector.RecordAsync("svc", "prod", "fp-old", now.AddMinutes(-10).AddSeconds(i), Guid.NewGuid());

        // 4 fresh errors — should never hit 5 in-window because old ones don't count
        for (var i = 0; i < 4; i++)
        {
            var r = await _detector.RecordAsync("svc", "prod", "fp-old", now.AddSeconds(i), Guid.NewGuid());
            r.IsSpike.Should().BeFalse("old errors outside the 5-min window must not count toward the threshold");
        }
    }

    [Fact]
    public async Task IndependentFingerprints_DoNotCrossContaminate()
    {
        var now = DateTimeOffset.UtcNow;
        // 4 errors on fingerprint A
        for (var i = 0; i < 4; i++)
            await _detector.RecordAsync("svc", "prod", "fp-A", now.AddSeconds(i), Guid.NewGuid());

        // 1 error on fingerprint B — should not spike because B has no prior entries
        var r = await _detector.RecordAsync("svc", "prod", "fp-B", now.AddSeconds(5), Guid.NewGuid());
        r.IsSpike.Should().BeFalse();
        r.Count.Should().Be(1);
    }

    [Fact]
    public async Task WindowKey_ExpiresShortlyAfterTheWindow()
    {
        await _detector.RecordAsync("svc", "prod", "fp-ttl", DateTimeOffset.UtcNow, Guid.NewGuid());

        var ttl = await _mux.GetDatabase().KeyTimeToLiveAsync("spike:svc:prod:fp-ttl");

        ttl.Should().NotBeNull("an idle window must not stay in Redis forever");
        ttl!.Value.Should().BeGreaterThan(TimeSpan.FromMinutes(5)).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(6));
    }

    // Five signals of one burst arrive together, as they do when Detection consumes concurrently. Each
    // used to add itself, count five and delete the window, so the burst raised up to five anomalies.
    [Fact]
    public async Task ConcurrentBurstOfFive_RaisesExactlyOneSpike()
    {
        for (var round = 0; round < 20; round++)
        {
            var now = DateTimeOffset.UtcNow;
            var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(i =>
                _detector.RecordAsync("svc", "prod", $"fp-burst-{round}", now.AddMilliseconds(i), Guid.NewGuid())));

            results.Count(r => r.IsSpike).Should().Be(1, $"round {round}: one burst of five is one anomaly");
        }
    }

    [Fact]
    public async Task ConcurrentBurstOfTen_RaisesTwoSpikes()
    {
        for (var round = 0; round < 10; round++)
        {
            var now = DateTimeOffset.UtcNow;
            var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
                _detector.RecordAsync("svc", "prod", $"fp-ten-{round}", now.AddMilliseconds(i), Guid.NewGuid())));

            results.Count(r => r.IsSpike).Should().Be(2, $"round {round}: every five in the window is one anomaly");
        }
    }
}
