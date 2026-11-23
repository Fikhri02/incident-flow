using FluentAssertions;
using IncidentFlow.BuildingBlocks.Contracts;
using IncidentFlow.Services.Correlation.Worker.Correlation;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace IncidentFlow.IntegrationTests.Correlation;

public sealed class CorrelationEngineTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder().Build();
    private CorrelationEngine _engine = null!;
    private IConnectionMultiplexer _mux = null!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        var mux = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
        _mux = mux;
        _engine = new CorrelationEngine(mux);
    }

    public async Task DisposeAsync() => await _redis.DisposeAsync();

    [Fact]
    public async Task SingleIncident_ProducesNoCorrelation()
    {
        var matches = await _engine.RecordAsync(Incident(deploymentId: "d1", fingerprint: "fp1"));
        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task TwoIncidents_SameDeployment_WithinWindow_CorrelatesOnDeployment()
    {
        var now = DateTimeOffset.UtcNow;
        await _engine.RecordAsync(Incident(deploymentId: "d2", fingerprint: null, at: now));
        var matches = await _engine.RecordAsync(Incident(deploymentId: "d2", fingerprint: null, at: now.AddMinutes(3)));

        matches.Should().ContainSingle(m => m.Type == "deployment");
        matches.Single(m => m.Type == "deployment").IncidentIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task TwoIncidents_SameFingerprint_WithinWindow_CorrelatesOnFingerprint()
    {
        var now = DateTimeOffset.UtcNow;
        await _engine.RecordAsync(Incident(deploymentId: null, fingerprint: "fp-shared", at: now));
        var matches = await _engine.RecordAsync(Incident(deploymentId: null, fingerprint: "fp-shared", at: now.AddMinutes(4)));

        matches.Should().ContainSingle(m => m.Type == "fingerprint");
    }

    [Fact]
    public async Task TwoIncidents_SameDeployment_OutsideWindow_NoCorrelation()
    {
        var now = DateTimeOffset.UtcNow;
        // First incident is 15 minutes ago — outside the 10-minute window
        await _engine.RecordAsync(Incident(deploymentId: "d3", fingerprint: null, at: now.AddMinutes(-15)));
        var matches = await _engine.RecordAsync(Incident(deploymentId: "d3", fingerprint: null, at: now));

        matches.Should().BeEmpty("first incident is outside the 10-minute correlation window");
    }

    [Fact]
    public async Task IncidentMatchesBothDeploymentAndFingerprint_ReturnsTwoMatches()
    {
        var now = DateTimeOffset.UtcNow;
        await _engine.RecordAsync(Incident(deploymentId: "d4", fingerprint: "fp4", at: now));
        var matches = await _engine.RecordAsync(Incident(deploymentId: "d4", fingerprint: "fp4", at: now.AddMinutes(1)));

        matches.Should().HaveCount(2, "one match per correlation type (deployment + fingerprint)");
        matches.Select(m => m.Type).Should().BeEquivalentTo(["deployment", "fingerprint"]);
    }

    [Fact]
    public async Task IncidentsInDifferentEnvironments_DoNotCorrelate()
    {
        var now = DateTimeOffset.UtcNow;
        await _engine.RecordAsync(Incident(deploymentId: "d5", fingerprint: "fp5", at: now, env: "staging"));
        var matches = await _engine.RecordAsync(Incident(deploymentId: "d5", fingerprint: "fp5", at: now.AddMinutes(1), env: "production"));

        matches.Should().BeEmpty("correlation is scoped per environment");
    }

    private static IncidentCreated Incident(
        string? deploymentId,
        string? fingerprint,
        DateTimeOffset? at = null,
        string env = "prod") =>
        new(
            IncidentId: Guid.NewGuid(),
            ServiceName: "test-svc",
            Environment: env,
            Region: null,
            Fingerprint: fingerprint,
            DeploymentId: deploymentId,
            Severity: "high",
            RuleName: "ErrorSpike",
            Summary: "test",
            DetectedAt: at ?? DateTimeOffset.UtcNow,
            CorrelationId: Guid.NewGuid().ToString("N")
        );

    [Fact]
    public async Task WindowKeys_ExpireShortlyAfterTheWindow()
    {
        await _engine.RecordAsync(Incident(deploymentId: "d-ttl", fingerprint: "fp-ttl"));
        var db = _mux.GetDatabase();

        foreach (var key in new[] { "corr:deploy:prod:d-ttl", "corr:fingerprint:prod:fp-ttl" })
        {
            var ttl = await db.KeyTimeToLiveAsync(key);
            ttl.Should().NotBeNull($"{key} must not stay in Redis forever");
            ttl!.Value.Should().BeGreaterThan(TimeSpan.FromMinutes(10)).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(11));
        }
    }
}
