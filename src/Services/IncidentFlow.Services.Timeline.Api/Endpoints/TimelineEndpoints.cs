using System.Text.Json;
using IncidentFlow.Services.Timeline.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Services.Timeline.Api.Endpoints;

public static class TimelineEndpoints
{
    /// <summary>The most events one page of <c>GET /api/v1/timeline/events</c> returns.</summary>
    public const int MaxEventsPageSize = 500;

    public const int DefaultEventsPageSize = 100;

    public static void MapTimelineEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/timeline").WithTags("timeline");

        group.MapGet("/incidents/{incidentId:guid}", async (Guid incidentId, TimelineDbContext db) =>
            Results.Ok(await db.Entries
                .Where(e => e.IncidentId == incidentId)
                .OrderBy(e => e.OccurredAt)
                .ToListAsync()));

        group.MapGet("/correlation/{correlationId}", async (string correlationId, TimelineDbContext db) =>
            Results.Ok(await db.Entries
                .Where(e => e.CorrelationId == correlationId)
                .OrderBy(e => e.OccurredAt)
                .ToListAsync()));

        // Every stored event in recorded order, for replay (Reporting's rebuild). Pass the returned
        // nextCursor as `after` for the next page; an empty page means the reader has caught up.
        // The sequence is assigned on insert, so an entry whose transaction commits late can land behind a
        // cursor a live follower already passed; a replay from 0 sees every entry committed before it started.
        group.MapGet("/events", async (long? after, int? take, TimelineDbContext db, CancellationToken ct) =>
        {
            var cursor = after ?? 0;
            var size = take ?? DefaultEventsPageSize;
            if (cursor < 0 || size < 1)
                return Results.BadRequest(new { error = "after must be 0 or more and take 1 or more." });
            size = Math.Min(size, MaxEventsPageSize);

            var rows = await db.Entries.AsNoTracking()
                .Where(e => e.Sequence > cursor)
                .OrderBy(e => e.Sequence)
                .Take(size)
                .Select(e => new { e.Sequence, e.EventType, e.IncidentId, e.Payload, e.OccurredAt, e.RecordedAt })
                .ToListAsync(ct);

            var items = rows.Select(r => new TimelineEventResponse(
                    r.Sequence, r.EventType, r.IncidentId,
                    r.Payload is null ? null : JsonSerializer.Deserialize<JsonElement>(r.Payload),
                    r.OccurredAt, r.RecordedAt))
                .ToList();
            return Results.Ok(new TimelineEventsPage(items, items.Count == 0 ? cursor : items[^1].Sequence));
        });
    }
}

public sealed record TimelineEventsPage(IReadOnlyList<TimelineEventResponse> Items, long NextCursor);

/// <summary>One stored event; <see cref="Payload"/> is the original message, or null on entries older than payloads.</summary>
public sealed record TimelineEventResponse(
    long Sequence,
    string EventType,
    Guid? IncidentId,
    JsonElement? Payload,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt);
