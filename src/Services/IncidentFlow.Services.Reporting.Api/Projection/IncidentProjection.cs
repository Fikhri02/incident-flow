using IncidentFlow.BuildingBlocks.Contracts;
using IncidentFlow.Services.Reporting.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace IncidentFlow.Services.Reporting.Api.Projection;

/// <summary>
/// Folds lifecycle events into the IncidentReports read model. The consumers and the rebuild both use it.
/// Each event is one atomic upsert (INSERT .. ON CONFLICT DO UPDATE) that sets only its own columns, so:
/// an event that overtakes IncidentCreated creates the row and Created fills the rest later; applying an
/// event twice changes nothing; and two events for one incident consumed at the same moment cannot both
/// try to insert the row. Statements run in the DbContext's current transaction (the inbox's, or the rebuild's).
/// </summary>
public sealed class IncidentProjection(ReportingDbContext db)
{
    public const string DeploymentCorrelation = "deployment";

    public Task ApplyAsync(IncidentCreated e, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "IncidentReports" AS r
                ("IncidentId", "ServiceName", "Environment", "Severity", "DetectedAt", "DeploymentLinked")
            VALUES (@id, @service, @environment, @severity, @at, false)
            ON CONFLICT ("IncidentId") DO UPDATE SET
                "ServiceName" = EXCLUDED."ServiceName",
                "Environment" = EXCLUDED."Environment",
                "Severity" = EXCLUDED."Severity",
                "DetectedAt" = EXCLUDED."DetectedAt"
            """,
            [Id(e.IncidentId), Text("service", e.ServiceName), Text("environment", e.Environment),
             Text("severity", e.Severity), At(e.DetectedAt)],
            ct);

    public Task ApplyAsync(IncidentAcknowledged e, CancellationToken ct) =>
        Stamp("AcknowledgedAt", e.IncidentId, e.ServiceName, e.Environment, e.AcknowledgedAt, ct);

    public Task ApplyAsync(IncidentResolved e, CancellationToken ct) =>
        Stamp("ResolvedAt", e.IncidentId, e.ServiceName, e.Environment, e.ResolvedAt, ct);

    public Task ApplyAsync(IncidentClosed e, CancellationToken ct) =>
        Stamp("ClosedAt", e.IncidentId, e.ServiceName, e.Environment, e.ClosedAt, ct);

    /// <summary>A deployment correlation marks every incident it names as a deployment regression.</summary>
    public Task ApplyAsync(CorrelationFound e, CancellationToken ct)
    {
        if (!string.Equals(e.CorrelationType, DeploymentCorrelation, StringComparison.OrdinalIgnoreCase) ||
            e.IncidentIds.Count == 0)
            return Task.CompletedTask;

        return db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "IncidentReports" ("IncidentId", "DeploymentLinked")
            SELECT DISTINCT id, true FROM unnest(@ids) AS id
            ON CONFLICT ("IncidentId") DO UPDATE SET "DeploymentLinked" = true
            """,
            [new NpgsqlParameter("ids", e.IncidentIds.ToArray())],
            ct);
    }

    /// <summary>Applies any event this projection understands; anything else is ignored.</summary>
    public Task ApplyAsync(object message, CancellationToken ct) => message switch
    {
        IncidentCreated e => ApplyAsync(e, ct),
        IncidentAcknowledged e => ApplyAsync(e, ct),
        IncidentResolved e => ApplyAsync(e, ct),
        IncidentClosed e => ApplyAsync(e, ct),
        CorrelationFound e => ApplyAsync(e, ct),
        _ => Task.CompletedTask
    };

    // Sets one lifecycle time. The later events carry the service and environment too, which fill an
    // early row; IncidentCreated's values stay authoritative once it has arrived.
    private Task Stamp(string column, Guid incidentId, string serviceName, string environment,
        DateTimeOffset at, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(
            $"""
             INSERT INTO "IncidentReports" AS r
                 ("IncidentId", "ServiceName", "Environment", "{column}", "DeploymentLinked")
             VALUES (@id, @service, @environment, @at, false)
             ON CONFLICT ("IncidentId") DO UPDATE SET
                 "{column}" = EXCLUDED."{column}",
                 "ServiceName" = COALESCE(r."ServiceName", EXCLUDED."ServiceName"),
                 "Environment" = COALESCE(r."Environment", EXCLUDED."Environment")
             """,
            [Id(incidentId), Text("service", serviceName), Text("environment", environment), At(at)],
            ct);

    private static NpgsqlParameter Id(Guid id) => new("id", id);

    // The contracts say non-nullable, but a payload can still carry null; Npgsql only accepts DBNull for it.
    private static NpgsqlParameter Text(string name, string? value) =>
        new(name, NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value };

    // timestamptz only takes UTC offsets.
    private static NpgsqlParameter At(DateTimeOffset at) => new("at", at.ToUniversalTime());
}
