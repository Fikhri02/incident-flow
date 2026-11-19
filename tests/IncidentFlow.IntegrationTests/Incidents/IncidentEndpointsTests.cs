extern alias incidentapi;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IncidentFlow.BuildingBlocks.Contracts;
using incidentapi::IncidentFlow.Services.Incident.Api.Domain;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IncidentFlow.IntegrationTests.Incidents;

// The Incident API end to end against real PostgreSQL: aggregate rules surface as the same
// HTTP codes and messages as before, the JSON shape is unchanged, and events are published.
public sealed class IncidentEndpointsTests(IncidentApiFactory factory) : IClassFixture<IncidentApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly string[] ResponseFields =
    [
        "incidentId", "serviceName", "environment", "region", "fingerprint", "deploymentId",
        "severity", "ruleName", "summary", "status", "detectedAt", "acknowledgedAt",
        "acknowledgedBy", "resolvedAt", "resolvedBy", "closedAt", "correlationId",
        "signalIdsJson", "createdAt"
    ];

    private async Task<Incident> SeedOpenIncident(string? fingerprint = null)
    {
        using var scope = factory.Services.CreateScope();
        var incidents = scope.ServiceProvider.GetRequiredService<IIncidentRepository>();
        var key = new IncidentKey($"svc-{Guid.NewGuid():N}", "prod", fingerprint ?? "db-timeout");
        var incident = Incident.Open(key, new IncidentDetails(
            null, "deploy-1", "high", "ErrorSpike", "spike", DateTimeOffset.UtcNow,
            $"corr-{Guid.NewGuid():N}", [Guid.NewGuid()]), DateTimeOffset.UtcNow);
        incidents.Add(incident);
        await incidents.SaveChangesAsync();
        return incident;
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;

    [Fact]
    public async Task Get_ReturnsTheSameJsonShapeAsBefore()
    {
        var incident = await SeedOpenIncident();

        var response = await _client.GetAsync($"/api/v1/incidents/{incident.IncidentId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Body(response);
        body.EnumerateObject().Select(p => p.Name).Should().Equal(ResponseFields);
        body.GetProperty("status").GetString().Should().Be("open");
    }

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/api/v1/incidents/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Acknowledge_Open_Returns200_PersistsAndPublishes()
    {
        var incident = await SeedOpenIncident();

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.IncidentId}/acknowledge", new { acknowledgedBy = "aina" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Body(response);
        body.GetProperty("status").GetString().Should().Be("acknowledged");
        body.GetProperty("acknowledgedBy").GetString().Should().Be("aina");

        var reloaded = await _client.GetFromJsonAsync<JsonDocument>($"/api/v1/incidents/{incident.IncidentId}");
        reloaded!.RootElement.GetProperty("status").GetString().Should().Be("acknowledged");

        factory.Bus.Published.OfType<IncidentAcknowledged>()
            .Should().ContainSingle(e => e.IncidentId == incident.IncidentId)
            .Which.AcknowledgedBy.Should().Be("aina");
    }

    [Fact]
    public async Task Acknowledge_Twice_Returns409_WithTheSameMessage()
    {
        var incident = await SeedOpenIncident();
        var url = $"/api/v1/incidents/{incident.IncidentId}/acknowledge";
        await _client.PostAsJsonAsync(url, new { acknowledgedBy = "aina" });

        var second = await _client.PostAsJsonAsync(url, new { acknowledgedBy = "ravi" });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Body(second)).GetProperty("error").GetString()
            .Should().Be("Cannot acknowledge incident in status 'acknowledged'.");
    }

    [Fact]
    public async Task Acknowledge_WithoutAName_Returns400()
    {
        var incident = await SeedOpenIncident();

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.IncidentId}/acknowledge", new { acknowledgedBy = " " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Body(response)).GetProperty("error").GetString().Should().Be("Acknowledged by is required.");
    }

    [Fact]
    public async Task Resolve_Then_ResolveAgain_Returns409()
    {
        var incident = await SeedOpenIncident();
        var url = $"/api/v1/incidents/{incident.IncidentId}/resolve";

        (await _client.PostAsJsonAsync(url, new { resolvedBy = "ravi" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await _client.PostAsJsonAsync(url, new { resolvedBy = "ravi" });

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Body(again)).GetProperty("error").GetString().Should().Be("Incident is already 'resolved'.");
    }

    [Fact]
    public async Task Close_FromOpen_Then_CloseAgain_Returns409()
    {
        var incident = await SeedOpenIncident();
        var url = $"/api/v1/incidents/{incident.IncidentId}/close";

        var first = await _client.PostAsync(url, null);
        var again = await _client.PostAsync(url, null);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Body(first)).GetProperty("status").GetString().Should().Be("closed");
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Body(again)).GetProperty("error").GetString().Should().Be("Incident is already closed.");
        factory.Bus.Published.OfType<IncidentClosed>()
            .Should().ContainSingle(e => e.IncidentId == incident.IncidentId);
    }

    [Fact]
    public async Task OpenList_HasActiveIncidentsOnly()
    {
        var open = await SeedOpenIncident();
        var resolved = await SeedOpenIncident();
        await _client.PostAsJsonAsync($"/api/v1/incidents/{resolved.IncidentId}/resolve", new { resolvedBy = "ravi" });

        var list = await _client.GetFromJsonAsync<JsonDocument>("/api/v1/incidents/open");
        var ids = list!.RootElement.EnumerateArray().Select(e => e.GetProperty("incidentId").GetGuid()).ToList();

        ids.Should().Contain(open.IncidentId).And.NotContain(resolved.IncidentId);
    }

    [Fact]
    public async Task Repository_FindsTheActiveIncidentForAKey_UntilItIsResolved()
    {
        var incident = await SeedOpenIncident(fingerprint: $"fp-{Guid.NewGuid():N}");

        using (var scope = factory.Services.CreateScope())
        {
            var incidents = scope.ServiceProvider.GetRequiredService<IIncidentRepository>();
            (await incidents.FindActiveAsync(incident.Key))!.IncidentId.Should().Be(incident.IncidentId);
        }

        await _client.PostAsJsonAsync($"/api/v1/incidents/{incident.IncidentId}/resolve", new { resolvedBy = "ravi" });

        using (var scope = factory.Services.CreateScope())
        {
            var incidents = scope.ServiceProvider.GetRequiredService<IIncidentRepository>();
            (await incidents.FindActiveAsync(incident.Key)).Should().BeNull();
        }
    }
}
