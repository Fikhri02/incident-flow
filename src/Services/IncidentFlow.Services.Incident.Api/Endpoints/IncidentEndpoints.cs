using IncidentFlow.BuildingBlocks.Contracts;
using IncidentFlow.BuildingBlocks.EventBus;
using IncidentFlow.Services.Incident.Api.Data;
using IncidentFlow.Services.Incident.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Services.Incident.Api.Endpoints;

public static class IncidentEndpoints
{
    public static void MapIncidentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/incidents").WithTags("incidents");

        group.MapGet("/", async (IncidentDbContext db) =>
            Results.Ok(await db.Incidents.OrderByDescending(i => i.DetectedAt).Take(100).ToListAsync()));

        group.MapGet("/open", async (IncidentDbContext db) =>
            Results.Ok(await db.Incidents
                .Where(i => i.Status == IncidentStatus.Open || i.Status == IncidentStatus.Acknowledged)
                .OrderByDescending(i => i.DetectedAt)
                .ToListAsync()));

        group.MapGet("/{id:guid}", async (Guid id, IncidentDbContext db) =>
        {
            var incident = await db.Incidents.FindAsync(id);
            return incident is null
                ? Results.NotFound(new { error = "Incident not found." })
                : Results.Ok(incident);
        });

        group.MapPost("/{id:guid}/acknowledge", async (Guid id, AcknowledgeRequest req, IncidentDbContext db, IEventBus bus) =>
        {
            var incident = await db.Incidents.FindAsync(id);
            if (incident is null) return Results.NotFound(new { error = "Incident not found." });
            if (incident.Status != IncidentStatus.Open)
                return Results.Conflict(new { error = $"Cannot acknowledge incident in status '{incident.Status}'." });

            incident.Status = IncidentStatus.Acknowledged;
            incident.AcknowledgedAt = DateTimeOffset.UtcNow;
            incident.AcknowledgedBy = req.AcknowledgedBy;
            await db.SaveChangesAsync();

            await bus.PublishAsync(new IncidentAcknowledged(
                incident.IncidentId, incident.ServiceName, incident.Environment,
                req.AcknowledgedBy, incident.AcknowledgedAt!.Value, incident.CorrelationId));

            return Results.Ok(incident);
        });

        group.MapPost("/{id:guid}/resolve", async (Guid id, ResolveRequest req, IncidentDbContext db, IEventBus bus) =>
        {
            var incident = await db.Incidents.FindAsync(id);
            if (incident is null) return Results.NotFound(new { error = "Incident not found." });
            if (incident.Status == IncidentStatus.Resolved || incident.Status == IncidentStatus.Closed)
                return Results.Conflict(new { error = $"Incident is already '{incident.Status}'." });

            incident.Status = IncidentStatus.Resolved;
            incident.ResolvedAt = DateTimeOffset.UtcNow;
            incident.ResolvedBy = req.ResolvedBy;
            await db.SaveChangesAsync();

            await bus.PublishAsync(new IncidentResolved(
                incident.IncidentId, incident.ServiceName, incident.Environment,
                req.ResolvedBy, incident.ResolvedAt!.Value, incident.CorrelationId));

            return Results.Ok(incident);
        });

        group.MapPost("/{id:guid}/close", async (Guid id, IncidentDbContext db, IEventBus bus) =>
        {
            var incident = await db.Incidents.FindAsync(id);
            if (incident is null) return Results.NotFound(new { error = "Incident not found." });
            if (incident.Status == IncidentStatus.Closed)
                return Results.Conflict(new { error = "Incident is already closed." });

            incident.Status = IncidentStatus.Closed;
            incident.ClosedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            await bus.PublishAsync(new IncidentClosed(
                incident.IncidentId, incident.ServiceName, incident.Environment,
                incident.ClosedAt!.Value, incident.CorrelationId));

            return Results.Ok(incident);
        });
    }
}

public sealed record AcknowledgeRequest(string AcknowledgedBy);
public sealed record ResolveRequest(string ResolvedBy);
