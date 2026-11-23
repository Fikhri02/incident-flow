extern alias incidentapi;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using IncidentFlow.IntegrationTests.Shared;
using incidentapi::IncidentFlow.Services.Incident.Api.Domain;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IncidentFlow.IntegrationTests.Incidents;

/// <summary>The Incident API with its real MassTransit event bus; the bus is never started.</summary>
public sealed class IncidentOutboxApiFactory : IncidentApiFactory
{
    protected override bool RecordEvents => false;
}

// A lifecycle command saves the incident and its integration event in one transaction: with no
// broker running, the event waits in OutboxMessage next to the changed incident.
public sealed class IncidentOutboxTests(IncidentOutboxApiFactory factory) : IClassFixture<IncidentOutboxApiFactory>
{
    private readonly HttpClient _client = CreateClient(factory);

    private static HttpClient CreateClient(IncidentOutboxApiFactory factory)
    {
        var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    private async Task<Incident> SeedOpenIncident(string correlationId)
    {
        using var scope = factory.Services.CreateScope();
        var incidents = scope.ServiceProvider.GetRequiredService<IIncidentRepository>();
        var incident = Incident.Open(new IncidentKey($"svc-{Guid.NewGuid():N}", "prod", "db-timeout"),
            new IncidentDetails(null, null, "high", "ErrorSpike", "spike", DateTimeOffset.UtcNow,
                correlationId, [Guid.NewGuid()]), DateTimeOffset.UtcNow);
        incidents.Add(incident);
        incident.DequeueDomainEvents();
        await incidents.SaveChangesAsync();
        return incident;
    }

    [Fact]
    public async Task Acknowledge_WritesTheEventToTheOutbox_WithTheIncident()
    {
        var correlationId = Guid.NewGuid();
        var incident = await SeedOpenIncident(correlationId.ToString("N"));

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.IncidentId}/acknowledge", new { acknowledgedBy = "alice" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await OutboxRows.WhereBodyContainsAsync(factory.ConnectionString, incident.IncidentId.ToString());
        rows.Should().ContainSingle();
        rows[0].MessageType.Should().Contain("IncidentAcknowledged");
        (await OutboxRows.CountAsync(factory.ConnectionString,
            """SELECT count(*) FROM "Incidents" WHERE "IncidentId" = @id AND "Status" = 'acknowledged'""",
            ("id", incident.IncidentId))).Should().Be(1);
    }

    [Fact]
    public async Task PublishedEvent_CarriesTheIncidentCorrelationIdInTheEnvelope()
    {
        var correlationId = Guid.NewGuid();
        var incident = await SeedOpenIncident(correlationId.ToString("N"));

        var response = await _client.PostAsync($"/api/v1/incidents/{incident.IncidentId}/close", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await OutboxRows.WhereBodyContainsAsync(factory.ConnectionString, incident.IncidentId.ToString());
        rows.Should().ContainSingle().Which.CorrelationId.Should().Be(correlationId);
    }
}
