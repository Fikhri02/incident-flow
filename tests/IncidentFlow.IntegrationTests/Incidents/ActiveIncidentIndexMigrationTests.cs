extern alias incidentapi;

using FluentAssertions;
using IncidentFlow.IntegrationTests.Shared;
using incidentapi::IncidentFlow.Services.Incident.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace IncidentFlow.IntegrationTests.Incidents;

// Before the hardening work nothing stopped two active incidents with the same key (a race in the
// consumer). The migration that adds the unique index must first close the extras, or Incident.Api
// fails at startup on a database that holds such a pair.
public sealed class ActiveIncidentIndexMigrationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string MigrationBeforeIndex = "20260517223042_InitialCreate";

    private static IncidentDbContext Context(string connectionString) =>
        new(new DbContextOptionsBuilder<IncidentDbContext>().UseNpgsql(connectionString).Options);

    private static async Task InsertAsync(NpgsqlConnection conn, Guid id, string service, string? fingerprint,
        string status, DateTimeOffset createdAt)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO "Incidents" ("IncidentId", "ServiceName", "Environment", "Fingerprint", "Severity", "RuleName",
                "Summary", "Status", "DetectedAt", "CorrelationId", "SignalIdsJson", "CreatedAt")
            VALUES (@id, @service, 'prod', @fingerprint, 'high', 'ErrorSpike', 'spike', @status, @createdAt,
                'corr', '[]', @createdAt)
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("service", service);
        cmd.Parameters.AddWithValue("fingerprint", (object?)fingerprint ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("createdAt", createdAt);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<Dictionary<Guid, (string Status, DateTimeOffset? ClosedAt)>> ReadAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand("""SELECT "IncidentId", "Status", "ClosedAt" FROM "Incidents" """, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new Dictionary<Guid, (string, DateTimeOffset?)>();
        while (await reader.ReadAsync())
            rows[reader.GetGuid(0)] = (reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2));
        return rows;
    }

    [Fact]
    public async Task Migrating_ADatabaseWithDuplicateActiveIncidents_KeepsTheEarliestActive_AndClosesTheRest()
    {
        var connectionString = postgres.NewDatabase();
        await using (var db = Context(connectionString))
            await db.GetService<IMigrator>().MigrateAsync(MigrationBeforeIndex);

        var t0 = DateTimeOffset.UtcNow.AddHours(-3);
        var earliest = Guid.NewGuid();
        var duplicate = Guid.NewGuid();
        var nullEarliest = Guid.NewGuid();
        var nullDuplicate = Guid.NewGuid();
        var otherKey = Guid.NewGuid();
        var resolved = Guid.NewGuid();
        await using (var conn = new NpgsqlConnection(connectionString))
        {
            await conn.OpenAsync();
            await InsertAsync(conn, earliest, "payment-api", "db-timeout", "open", t0);
            await InsertAsync(conn, duplicate, "payment-api", "db-timeout", "acknowledged", t0.AddMinutes(5));
            // A null fingerprint is a key value too: these two collide under the index (nulls not distinct).
            await InsertAsync(conn, nullEarliest, "payment-api", null, "acknowledged", t0);
            await InsertAsync(conn, nullDuplicate, "payment-api", null, "open", t0.AddMinutes(1));
            await InsertAsync(conn, otherKey, "checkout-api", "db-timeout", "open", t0.AddMinutes(2));
            await InsertAsync(conn, resolved, "payment-api", "db-timeout", "resolved", t0.AddMinutes(-30));
        }

        await using (var db = Context(connectionString))
        {
            var migrate = () => db.Database.MigrateAsync();
            await migrate.Should().NotThrowAsync("existing duplicates must not block the unique index");
        }

        await using (var conn = new NpgsqlConnection(connectionString))
        {
            await conn.OpenAsync();
            var rows = await ReadAsync(conn);
            rows[earliest].Status.Should().Be("open");
            rows[nullEarliest].Status.Should().Be("acknowledged");
            rows[otherKey].Status.Should().Be("open");
            rows[resolved].Status.Should().Be("resolved");
            rows[resolved].ClosedAt.Should().BeNull();
            foreach (var id in new[] { duplicate, nullDuplicate })
            {
                rows[id].Status.Should().Be("closed");
                rows[id].ClosedAt.Should().NotBeNull("a closed incident records when it was closed");
            }

            rows.Values.Count(r => r.Status is "open" or "acknowledged").Should().Be(3, "one active per key");
        }
    }
}
