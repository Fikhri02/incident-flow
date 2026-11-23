extern alias incidentapi;

using FluentAssertions;
using IncidentFlow.BuildingBlocks.Contracts;
using IncidentFlow.BuildingBlocks.EventBus;
using IncidentFlow.IntegrationTests.Shared;
using incidentapi::IncidentFlow.Services.Incident.Api.Data;
using incidentapi::IncidentFlow.Services.Incident.Api.Domain;
using incidentapi::IncidentFlow.Services.Incident.Api.Messaging;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IncidentFlow.IntegrationTests.Incidents;

// The AnomalyDetected consumer against real PostgreSQL and the service's own MassTransit registration:
// a redelivered message is ignored (inbox), and two anomalies for one key open one incident
// even when both pass the "is there an active incident?" check (unique index).
public sealed class IncidentMessagingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    /// <summary>Simulates the race: every anomaly believes no incident is active for its key.</summary>
    private sealed class RacingRepository(IncidentRepository inner) : IIncidentRepository
    {
        public Task<Incident?> GetAsync(Guid incidentId, CancellationToken cancellationToken = default) =>
            inner.GetAsync(incidentId, cancellationToken);

        public Task<Incident?> FindActiveAsync(IncidentKey key, CancellationToken cancellationToken = default) =>
            Task.FromResult<Incident?>(null);

        public void Add(Incident incident) => inner.Add(incident);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            inner.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Stands in for Incident's subscribers. The outbox delivers a serialized body, which the harness
    /// does not list under Published, so the tests watch what arrives instead.
    /// </summary>
    private sealed class IncidentCreatedProbe : IConsumer<IncidentCreated>
    {
        public Task Consume(ConsumeContext<IncidentCreated> context) => Task.CompletedTask;
    }

    private async Task<ServiceProvider> StartAsync(bool racing = false)
    {
        var database = postgres.NewDatabase();
        var services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<IncidentDbContext>(o => o.UseNpgsql(database, n => n.EnableRetryOnFailure()))
            .AddSingleton(TimeProvider.System)
            .AddScoped<IEventBus, MassTransitEventBus>()
            .AddScoped<IncidentEventPublisher>()
            .AddScoped<IncidentRepository>();

        if (racing) services.AddScoped<IIncidentRepository, RacingRepository>();
        else services.AddScoped<IIncidentRepository>(sp => sp.GetRequiredService<IncidentRepository>());

        services.AddMassTransitTestHarness(x =>
        {
            x.AddIncidentMessaging();
            x.AddConsumer<IncidentCreatedProbe>();
            x.UsingTestTransport();
        });

        var provider = services.BuildServiceProvider(true);
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IncidentDbContext>().Database.MigrateAsync();

        await provider.GetRequiredService<ITestHarness>().Start();
        return provider;
    }

    private static AnomalyDetected Anomaly(string service, string correlationId) =>
        new(Guid.NewGuid(), service, "prod", null, "db-timeout", "deploy-1", "high", "ErrorSpike",
            "5 errors in 5 min", DateTimeOffset.UtcNow, [Guid.NewGuid()], correlationId);

    private static async Task<List<Incident>> IncidentsFor(IServiceProvider provider, string service)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IncidentDbContext>().Incidents.AsNoTracking()
            .Where(i => i.ServiceName == service).ToListAsync();
    }

    [Fact]
    public async Task RedeliveredAnomaly_DoesNotOpenASecondIncident()
    {
        await using var provider = await StartAsync();
        var harness = provider.GetRequiredService<ITestHarness>();
        var service = $"svc-{Guid.NewGuid():N}";
        var anomaly = Anomaly(service, Guid.NewGuid().ToString("N"));
        var messageId = NewId.NextGuid();

        await harness.Bus.Publish(anomaly, ctx => ctx.MessageId = messageId);
        (await harness.Consumed.Any<IncidentCreated>()).Should().BeTrue();

        // Resolve it, so only the inbox (not the active-incident check) can stop a second incident.
        await using (var scope = provider.CreateAsyncScope())
        {
            var incidents = scope.ServiceProvider.GetRequiredService<IIncidentRepository>();
            var incident = (await IncidentsFor(provider, service)).Single();
            var loaded = (await incidents.GetAsync(incident.IncidentId))!;
            loaded.Resolve("tester", DateTimeOffset.UtcNow);
            await incidents.SaveChangesAsync();
        }

        await harness.Bus.Publish(anomaly, ctx => ctx.MessageId = messageId);
        await harness.InactivityTask;

        (await IncidentsFor(provider, service)).Should().ContainSingle();
    }

    [Fact]
    public async Task TwoSavesForTheSameKey_LeaveOneActiveIncident()
    {
        await using var provider = await StartAsync();
        var service = $"svc-{Guid.NewGuid():N}";
        var key = new IncidentKey(service, "prod", "db-timeout");
        IncidentDetails Details() => new(null, null, "high", "ErrorSpike", "spike", DateTimeOffset.UtcNow,
            Guid.NewGuid().ToString("N"), [Guid.NewGuid()]);

        // Both writers checked for an active incident before either saved.
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var firstRepo = first.ServiceProvider.GetRequiredService<IIncidentRepository>();
        var secondRepo = second.ServiceProvider.GetRequiredService<IIncidentRepository>();
        (await firstRepo.FindActiveAsync(key)).Should().BeNull();
        (await secondRepo.FindActiveAsync(key)).Should().BeNull();

        firstRepo.Add(Incident.Open(key, Details(), DateTimeOffset.UtcNow));
        secondRepo.Add(Incident.Open(key, Details(), DateTimeOffset.UtcNow));
        await firstRepo.SaveChangesAsync();
        var secondSave = () => secondRepo.SaveChangesAsync();

        await secondSave.Should().ThrowAsync<Exception>();
        (await IncidentsFor(provider, service)).Should().ContainSingle(i => i.Status.IsActive());
    }

    [Fact]
    public async Task ConcurrentAnomalies_OpenOneIncident_AndTheLoserIsNotAFault()
    {
        await using var provider = await StartAsync(racing: true);
        var harness = provider.GetRequiredService<ITestHarness>();
        var service = $"svc-{Guid.NewGuid():N}";

        await harness.Bus.Publish(Anomaly(service, Guid.NewGuid().ToString("N")));
        await harness.Bus.Publish(Anomaly(service, Guid.NewGuid().ToString("N")));
        await harness.InactivityTask;

        (await IncidentsFor(provider, service)).Should().ContainSingle();
        harness.Consumed.Select<IncidentCreated>().Count(m => m.Context.Message.ServiceName == service)
            .Should().Be(1);
        harness.Published.Select<Fault<AnomalyDetected>>().Should().BeEmpty();
    }

    [Fact]
    public async Task IncidentCreated_CarriesTheAnomalyCorrelationIdInTheEnvelope()
    {
        await using var provider = await StartAsync();
        var harness = provider.GetRequiredService<ITestHarness>();
        var service = $"svc-{Guid.NewGuid():N}";
        var correlationId = Guid.NewGuid();

        await harness.Bus.Publish(Anomaly(service, correlationId.ToString("N")));

        (await harness.Consumed.Any<IncidentCreated>(m => m.Context.Message.ServiceName == service)).Should().BeTrue();
        var created = harness.Consumed.Select<IncidentCreated>().Single(m => m.Context.Message.ServiceName == service);
        created.Context.CorrelationId.Should().Be(correlationId);
    }
}
