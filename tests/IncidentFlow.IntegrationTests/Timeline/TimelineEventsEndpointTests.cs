using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IncidentFlow.BuildingBlocks.Contracts;
using IncidentFlow.IntegrationTests.Reporting;
using IncidentFlow.IntegrationTests.Shared;
using IncidentFlow.Services.Timeline.Api.Consumers;
using IncidentFlow.Services.Timeline.Api.Data;
using IncidentFlow.Services.Timeline.Api.Endpoints;
using IncidentFlow.Services.Timeline.Api.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static IncidentFlow.IntegrationTests.Reporting.KnownIncidents;

namespace IncidentFlow.IntegrationTests.Timeline;

// GET /api/v1/timeline/events pages through every stored event in the order Timeline recorded them,
// with the original message as JSON, so another service (Reporting) can replay them.
public sealed class TimelineEventsEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Events_CarryTypePayloadAndTimes_InRecordedOrder()
    {
        var database = postgres.NewDatabase();
        await using var host = await ConsumerHost<TimelineDbContext>.StartAsync(database, x => x.AddTimelineMessaging());
        var id = Guid.NewGuid();
        var created = Created(id, "payment-api", "prod", "Critical");
        var correlation = Correlated("deployment", "prod", id);
        await host.PublishAsync(created);
        await host.PublishAsync(Acknowledged(id, "payment-api", "prod", 10));
        await host.PublishAsync(correlation);
        using var api = new TimelineApiFactory(database);

        var page = await api.CreateClient().GetFromJsonAsync<TimelinePageDto>("/api/v1/timeline/events");

        page!.Items.Select(i => i.EventType).Should().Equal("incident.created", "incident.acknowledged", "correlation.found");
        page.Items.Select(i => i.Sequence).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        page.Items[0].IncidentId.Should().Be(id);
        page.Items[0].OccurredAt.Should().Be(T0);
        page.Items[0].RecordedAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-5));
        page.Items[0].Payload!.Value.Deserialize<IncidentCreated>(Web).Should().Be(created);
        page.Items[2].Payload!.Value.Deserialize<CorrelationFound>(Web)!.IncidentIds.Should().Equal(id);
        page.NextCursor.Should().Be(page.Items[^1].Sequence);
    }

    [Fact]
    public async Task Cursor_WalksEveryEventOnce_AndStaysPutAtTheEnd()
    {
        var database = postgres.NewDatabase();
        await using var host = await ConsumerHost<TimelineDbContext>.StartAsync(database, x => x.AddTimelineMessaging());
        await host.PublishAsync(Events());
        using var api = new TimelineApiFactory(database);
        var client = api.CreateClient();

        var seen = new List<TimelineEventDto>();
        long cursor = 0;
        for (var i = 0; i < 10; i++)
        {
            var page = await client.GetFromJsonAsync<TimelinePageDto>($"/api/v1/timeline/events?after={cursor}&take=4");
            if (page!.Items.Count == 0)
            {
                page.NextCursor.Should().Be(cursor, "an empty page hands back the cursor it was given");
                break;
            }
            page.Items.Should().HaveCountLessThanOrEqualTo(4);
            page.Items.Should().OnlyContain(e => e.Sequence > cursor);
            seen.AddRange(page.Items);
            page.NextCursor.Should().Be(page.Items[^1].Sequence);
            cursor = page.NextCursor;
        }

        seen.Should().HaveCount(Events().Length);
        seen.Select(e => e.Sequence).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Take_DefaultsTo100_AndIsCappedAt500()
    {
        var database = postgres.NewDatabase();
        using var api = new TimelineApiFactory(database);
        var client = api.CreateClient();
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimelineDbContext>();
            for (var i = 0; i < TimelineEndpoints.MaxEventsPageSize + 1; i++)
                db.Entries.Add(new TimelineEntryEntity
                {
                    EntryId = Guid.NewGuid(), EventType = "signal.error", ServiceName = "svc", Environment = "prod",
                    Summary = $"error {i}", CorrelationId = "c", OccurredAt = T0
                });
            await db.SaveChangesAsync();
        }

        (await client.GetFromJsonAsync<TimelinePageDto>("/api/v1/timeline/events"))!.Items.Should().HaveCount(100);
        (await client.GetFromJsonAsync<TimelinePageDto>("/api/v1/timeline/events?take=100000"))!.Items
            .Should().HaveCount(TimelineEndpoints.MaxEventsPageSize);
        TimelineEndpoints.MaxEventsPageSize.Should().Be(500);
    }

    [Theory]
    [InlineData("?take=0")]
    [InlineData("?take=-1")]
    [InlineData("?after=-1")]
    public async Task InvalidPaging_IsABadRequest(string query)
    {
        using var api = new TimelineApiFactory(postgres.NewDatabase());

        var response = await api.CreateClient().GetAsync($"/api/v1/timeline/events{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
