using IncidentFlow.Services.Reporting.Api.Data;
using IncidentFlow.Services.Reporting.Api.Projection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace IncidentFlow.Services.Reporting.Api.Rebuild;

public sealed class RebuildOptions
{
    /// <summary>Events asked for per Timeline page. Timeline caps a page at 500.</summary>
    public int PageSize { get; set; } = 500;
}

public sealed record RebuildResult(int EventsApplied, int EventsWithoutPayload);

/// <summary>
/// Replays every event Timeline stored, in Timeline's order, through <see cref="IncidentProjection"/> — the
/// code the consumers run — committing each page in its own short transaction.
/// <para>
/// By default nothing is deleted: the projection's upserts are idempotent and set only their own columns, so
/// replaying over the read model fixes what Timeline knows and keeps rows Timeline has not stored yet (an event
/// Reporting consumed before Timeline did). <c>clean</c> empties the read model first, in the same transaction
/// as the first page; run it only once Timeline has caught up, and expect partial figures while it runs.
/// </para>
/// <para>
/// Live consumers keep running and never wait behind more than one page. A failed or interrupted rebuild leaves
/// the pages it committed, which is a consistent read model — running the rebuild again finishes the job.
/// A Postgres session advisory lock, held on its own connection for the whole run, keeps two rebuilds (on any
/// Reporting instance) from overlapping.
/// </para>
/// </summary>
public sealed class ReportRebuilder(
    ReportingDbContext db,
    TimelineClient timeline,
    IOptions<RebuildOptions> options,
    ILogger<ReportRebuilder> logger)
{
    /// <summary>The advisory lock key the rebuild holds while it runs ("REPORT" in ASCII).</summary>
    public const long AdvisoryLockKey = 0x5245504F5254;

    /// <summary>Runs the rebuild, or returns null without doing anything when another rebuild is running.</summary>
    public async Task<RebuildResult?> TryRebuildAsync(bool clean, CancellationToken ct)
    {
        await using var guard = new NpgsqlConnection(db.Database.GetConnectionString());
        await guard.OpenAsync(ct);
        if (!await AdvisoryAsync(guard, "pg_try_advisory_lock", ct))
            return null;

        try
        {
            return await ReplayAsync(clean, ct);
        }
        finally
        {
            // Unlocking keeps a pooled connection clean. If the connection is gone, Postgres has already
            // released the lock with the session, so a failure here must not hide the rebuild's own error.
            try
            {
                await AdvisoryAsync(guard, "pg_advisory_unlock", CancellationToken.None);
            }
            catch (NpgsqlException ex)
            {
                logger.LogWarning(ex, "Could not release the rebuild lock; it is released with its session");
            }
        }
    }

    private async Task<RebuildResult> ReplayAsync(bool clean, CancellationToken ct)
    {
        var pageSize = Math.Max(1, options.Value.PageSize);
        // The Aspire DbContext retries on transient failures; a user transaction must run inside its strategy.
        var strategy = db.Database.CreateExecutionStrategy();
        var projection = new IncidentProjection(db);
        var clearFirst = clean;
        long cursor = 0;
        int applied = 0, withoutPayload = 0;

        while (true)
        {
            // Read before writing: a Timeline that cannot be read leaves the read model untouched, even in clean mode.
            var page = await timeline.GetEventsAsync(cursor, pageSize, ct);
            if (page.Items.Count > 0 && page.NextCursor <= cursor)
                throw new InvalidOperationException($"Timeline's cursor did not advance past {cursor}.");

            var messages = new List<object>();
            foreach (var e in page.Items.Where(TimelineClient.IsReportingEvent))
            {
                if (TimelineClient.Message(e) is { } message)
                    messages.Add(message);
                else
                    withoutPayload++;
            }

            if (messages.Count > 0 || clearFirst)
            {
                var deleteFirst = clearFirst;
                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(ct);
                    if (deleteFirst)
                        await db.Incidents.ExecuteDeleteAsync(ct);
                    foreach (var message in messages)
                        await projection.ApplyAsync(message, ct);
                    await transaction.CommitAsync(ct);
                });
                clearFirst = false;
                applied += messages.Count;
            }

            if (page.Items.Count == 0) break;
            cursor = page.NextCursor;
        }

        if (withoutPayload > 0)
            logger.LogWarning(
                "Rebuild skipped {Count} Timeline events recorded before Timeline stored payloads", withoutPayload);
        logger.LogInformation("Rebuilt the reporting read model ({Mode}) from {Count} Timeline events",
            clean ? "clean" : "replay", applied);
        return new RebuildResult(applied, withoutPayload);
    }

    private static async Task<bool> AdvisoryAsync(NpgsqlConnection connection, string function, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"SELECT {function}(@key)", connection);
        command.Parameters.AddWithValue("key", AdvisoryLockKey);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }
}
