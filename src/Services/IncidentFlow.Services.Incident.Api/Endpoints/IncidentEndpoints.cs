using IncidentFlow.Services.Incident.Api.Data;
using IncidentFlow.Services.Incident.Api.Domain;
using IncidentFlow.Services.Incident.Api.Messaging;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Services.Incident.Api.Endpoints;

public static class IncidentEndpoints
{
    public static void MapIncidentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/incidents").WithTags("incidents");

        // Queries read straight from the DbContext; commands go through the aggregate.
        group.MapGet("/", async (IncidentDbContext db, CancellationToken ct) =>
        {
            var incidents = await db.Incidents.AsNoTracking()
                .OrderByDescending(i => i.DetectedAt).Take(100).ToListAsync(ct);
            return Results.Ok(incidents.Select(IncidentResponse.From));
        });

        group.MapGet("/open", async (IncidentDbContext db, CancellationToken ct) =>
        {
            var incidents = await db.Incidents.AsNoTracking()
                .Where(Domain.Incident.IsActive)
                .OrderByDescending(i => i.DetectedAt).ToListAsync(ct);
            return Results.Ok(incidents.Select(IncidentResponse.From));
        });

        group.MapGet("/{id:guid}", async (Guid id, IIncidentRepository incidents, CancellationToken ct) =>
        {
            var incident = await incidents.GetAsync(id, ct);
            return incident is null
                ? Results.NotFound(new { error = "Incident not found." })
                : Results.Ok(IncidentResponse.From(incident));
        });

        group.MapPost("/{id:guid}/acknowledge",
            (Guid id, AcknowledgeRequest req, IIncidentRepository incidents, IncidentEventPublisher events,
                TimeProvider clock, CancellationToken ct) =>
                ChangeAsync(id, incidents, events, i => i.Acknowledge(req.AcknowledgedBy, clock.GetUtcNow()), ct));

        group.MapPost("/{id:guid}/resolve",
            (Guid id, ResolveRequest req, IIncidentRepository incidents, IncidentEventPublisher events,
                TimeProvider clock, CancellationToken ct) =>
                ChangeAsync(id, incidents, events, i => i.Resolve(req.ResolvedBy, clock.GetUtcNow()), ct));

        group.MapPost("/{id:guid}/close",
            (Guid id, IIncidentRepository incidents, IncidentEventPublisher events,
                TimeProvider clock, CancellationToken ct) =>
                ChangeAsync(id, incidents, events, i => i.Close(clock.GetUtcNow()), ct));
    }

    /// <summary>Load the aggregate, let it apply the change, save, then publish what it recorded.</summary>
    private static async Task<IResult> ChangeAsync(
        Guid id,
        IIncidentRepository incidents,
        IncidentEventPublisher events,
        Action<Domain.Incident> change,
        CancellationToken ct)
    {
        var incident = await incidents.GetAsync(id, ct);
        if (incident is null) return Results.NotFound(new { error = "Incident not found." });

        try
        {
            change(incident);
        }
        catch (IncidentTransitionException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        await incidents.SaveChangesAsync(ct);
        await events.PublishAsync(incident, ct);
        return Results.Ok(IncidentResponse.From(incident));
    }
}

public sealed record AcknowledgeRequest(string AcknowledgedBy);
public sealed record ResolveRequest(string ResolvedBy);
