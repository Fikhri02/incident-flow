using IncidentFlow.Services.Timeline.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Services.Timeline.Api.Endpoints;

public static class TimelineEndpoints
{
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
    }
}
