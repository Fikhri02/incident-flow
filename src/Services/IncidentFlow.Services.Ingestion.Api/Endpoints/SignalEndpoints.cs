using IncidentFlow.BuildingBlocks.Contracts;
using IncidentFlow.BuildingBlocks.EventBus;
using IncidentFlow.Services.Ingestion.Api.Data;
using IncidentFlow.Services.Ingestion.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Services.Ingestion.Api.Endpoints;

public static class SignalEndpoints
{
    private const string CorrelationIdHeader = "x-correlation-id";

    public static void MapSignalEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/signals").WithTags("signals");

        group.MapPost("/errors", async (ErrorSignalRequest req, HttpContext ctx, IngestionDbContext db, IEventBus bus) =>
        {
            var result = await IngestAsync(db, bus, ctx, "error",
                req.SourceSystem, req.ServiceName, req.Environment, req.Region,
                req.Message, req.Fingerprint, req.TraceId, req.DeploymentId,
                req.OccurredAt, req.IdempotencyKey,
                (signalId, correlationId) => bus.PublishAsync(new ErrorSignalReceived(
                    signalId, req.SourceSystem, req.ServiceName, req.Environment, req.Region,
                    req.Fingerprint, req.Message, req.TraceId, req.DeploymentId,
                    req.OccurredAt ?? DateTimeOffset.UtcNow, correlationId, req.IdempotencyKey)));
            return Results.Json(result, statusCode: 202);
        });

        group.MapPost("/logs", async (LogSignalRequest req, HttpContext ctx, IngestionDbContext db, IEventBus bus) =>
        {
            var result = await IngestAsync(db, bus, ctx, "log",
                req.SourceSystem, req.ServiceName, req.Environment, req.Region,
                req.Message, req.Fingerprint, req.TraceId, null,
                req.OccurredAt, req.IdempotencyKey,
                (signalId, correlationId) => bus.PublishAsync(new LogSignalReceived(
                    signalId, req.SourceSystem, req.ServiceName, req.Environment, req.Region,
                    req.Severity, req.Message, req.Fingerprint, req.TraceId,
                    req.OccurredAt ?? DateTimeOffset.UtcNow, correlationId, req.IdempotencyKey)));
            return Results.Json(result, statusCode: 202);
        });

        group.MapPost("/metrics", async (MetricSignalRequest req, HttpContext ctx, IngestionDbContext db, IEventBus bus) =>
        {
            var result = await IngestAsync(db, bus, ctx, "metric",
                req.SourceSystem, req.ServiceName, req.Environment, req.Region,
                $"{req.MetricName}={req.Value}", null, null, null,
                req.OccurredAt, req.IdempotencyKey,
                (signalId, correlationId) => bus.PublishAsync(new MetricSignalReceived(
                    signalId, req.SourceSystem, req.ServiceName, req.Environment, req.Region,
                    req.MetricName, req.Value,
                    req.OccurredAt ?? DateTimeOffset.UtcNow, correlationId, req.IdempotencyKey)));
            return Results.Json(result, statusCode: 202);
        });

        group.MapPost("/deployments", async (DeploymentSignalRequest req, HttpContext ctx, IngestionDbContext db, IEventBus bus) =>
        {
            var result = await IngestAsync(db, bus, ctx, "deployment",
                req.SourceSystem, req.ServiceName, req.Environment, req.Region,
                $"Deployed {req.Version}", null, null, req.DeploymentId,
                req.OccurredAt, req.IdempotencyKey,
                (signalId, correlationId) => bus.PublishAsync(new DeploymentSignalReceived(
                    signalId, req.SourceSystem, req.ServiceName, req.Environment, req.Region,
                    req.DeploymentId, req.Version,
                    req.OccurredAt ?? DateTimeOffset.UtcNow, correlationId, req.IdempotencyKey)));
            return Results.Json(result, statusCode: 202);
        });

        group.MapPost("/health-checks", async (HealthCheckSignalRequest req, HttpContext ctx, IngestionDbContext db, IEventBus bus) =>
        {
            var result = await IngestAsync(db, bus, ctx, "health-check",
                req.SourceSystem, req.ServiceName, req.Environment, req.Region,
                req.IsHealthy ? "Health check passed" : req.Reason ?? "Health check failed", null, null, null,
                req.OccurredAt, req.IdempotencyKey,
                (signalId, correlationId) => bus.PublishAsync(new HealthCheckSignalReceived(
                    signalId, req.SourceSystem, req.ServiceName, req.Environment, req.Region,
                    req.IsHealthy, req.Reason,
                    req.OccurredAt ?? DateTimeOffset.UtcNow, correlationId, req.IdempotencyKey)));
            return Results.Json(result, statusCode: 202);
        });

        group.MapGet("/", async (IngestionDbContext db) =>
            Results.Ok(await db.Signals.OrderByDescending(s => s.ReceivedAt).Take(100).ToListAsync()));
    }

    private static async Task<SignalAcceptedResponse> IngestAsync(
        IngestionDbContext db,
        IEventBus bus,
        HttpContext ctx,
        string signalType,
        string sourceSystem, string serviceName, string environment, string? region,
        string summary, string? fingerprint, string? traceId, string? deploymentId,
        DateTimeOffset? occurredAt, string? idempotencyKey,
        Func<Guid, string, Task> publish)
    {
        var correlationId = ctx.Request.Headers[CorrelationIdHeader].FirstOrDefault()
                            ?? Guid.NewGuid().ToString("N");

        if (idempotencyKey is not null)
        {
            var existing = await db.Signals
                .FirstOrDefaultAsync(s => s.IdempotencyKey == idempotencyKey && s.SignalType == signalType);
            if (existing is not null)
                return new SignalAcceptedResponse(existing.SignalId, correlationId, true, "Duplicate signal suppressed.");
        }

        var signal = new SignalEntity
        {
            SignalId = Guid.NewGuid(),
            SignalType = signalType,
            SourceSystem = sourceSystem,
            ServiceName = serviceName,
            Environment = environment,
            Region = region,
            Fingerprint = fingerprint,
            TraceId = traceId,
            DeploymentId = deploymentId,
            OccurredAt = occurredAt ?? DateTimeOffset.UtcNow,
            Summary = summary,
            IdempotencyKey = idempotencyKey,
            CorrelationId = correlationId,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        db.Signals.Add(signal);
        await db.SaveChangesAsync();
        await publish(signal.SignalId, correlationId);

        return new SignalAcceptedResponse(signal.SignalId, correlationId, false, "Signal accepted.");
    }
}

// Request DTOs (mirrors BuildingBlocks contracts for HTTP surface)
public sealed record ErrorSignalRequest(
    string SourceSystem, string ServiceName, string Environment,
    string Message, string Fingerprint,
    DateTimeOffset? OccurredAt = null, string? Region = null,
    string? TraceId = null, string? DeploymentId = null,
    Dictionary<string, string>? Metadata = null, string? IdempotencyKey = null);

public sealed record LogSignalRequest(
    string SourceSystem, string ServiceName, string Environment,
    string Severity, string Message,
    DateTimeOffset? OccurredAt = null, string? Region = null,
    string? TraceId = null, string? Fingerprint = null,
    string? DeploymentId = null, Dictionary<string, string>? Metadata = null,
    string? IdempotencyKey = null);

public sealed record MetricSignalRequest(
    string SourceSystem, string ServiceName, string Environment,
    string MetricName, double Value,
    DateTimeOffset? OccurredAt = null, string? Region = null,
    Dictionary<string, string>? Metadata = null, string? IdempotencyKey = null);

public sealed record DeploymentSignalRequest(
    string SourceSystem, string ServiceName, string Environment,
    string DeploymentId, string Version,
    DateTimeOffset? OccurredAt = null, string? Region = null,
    Dictionary<string, string>? Metadata = null, string? IdempotencyKey = null);

public sealed record HealthCheckSignalRequest(
    string SourceSystem, string ServiceName, string Environment,
    bool IsHealthy, string? Reason = null,
    DateTimeOffset? OccurredAt = null, string? Region = null,
    Dictionary<string, string>? Metadata = null, string? IdempotencyKey = null);

public sealed record SignalAcceptedResponse(
    Guid SignalId, string CorrelationId, bool Duplicate, string Message);
