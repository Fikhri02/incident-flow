using System.Net.Http.Json;
using FluentAssertions;
using IncidentFlow.IntegrationTests.Reporting;
using IncidentFlow.IntegrationTests.Shared;
using IncidentFlow.Services.Timeline.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace IncidentFlow.IntegrationTests.Timeline;

// Entries recorded before Timeline had a sequence get one in the order they were recorded, keep a null
// payload, and new entries continue after them.
public sealed class TimelineSequenceMigrationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string MigrationBeforeSequence = "20261123023412_AddMassTransitInbox";

    [Fact]
    public async Task ExistingEntries_AreNumberedInRecordedOrder_AndNewOnesFollow()
    {
        var connectionString = postgres.NewDatabase();
        await using (var db = new TimelineDbContext(
                         new DbContextOptionsBuilder<TimelineDbContext>().UseNpgsql(connectionString).Options))
            await db.GetService<IMigrator>().MigrateAsync(MigrationBeforeSequence);

        var t0 = DateTimeOffset.UtcNow.AddHours(-1);
        await using (var conn = new NpgsqlConnection(connectionString))
        {
            await conn.OpenAsync();
            // Inserted out of recorded order on purpose.
            foreach (var (summary, recordedAt) in new[] { ("second", t0.AddMinutes(2)), ("first", t0.AddMinutes(1)), ("third", t0.AddMinutes(3)) })
            {
                await using var cmd = new NpgsqlCommand(
                    """
                    INSERT INTO "Entries" ("EntryId", "EventType", "ServiceName", "Environment", "Summary",
                        "CorrelationId", "OccurredAt", "RecordedAt")
                    VALUES (@id, 'signal.error', 'svc', 'prod', @summary, 'c', @at, @at)
                    """, conn);
                cmd.Parameters.AddWithValue("id", Guid.NewGuid());
                cmd.Parameters.AddWithValue("summary", summary);
                cmd.Parameters.AddWithValue("at", recordedAt);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        using var api = new TimelineApiFactory(connectionString); // migrates to the latest at startup
        await using (var db = new TimelineDbContext(
                         new DbContextOptionsBuilder<TimelineDbContext>().UseNpgsql(connectionString).Options))
        {
            await db.Database.MigrateAsync();
            db.Entries.Add(new() { EntryId = Guid.NewGuid(), EventType = "signal.error", ServiceName = "svc",
                Environment = "prod", Summary = "fourth", CorrelationId = "c", OccurredAt = t0 });
            await db.SaveChangesAsync();
        }

        var page = await api.CreateClient().GetFromJsonAsync<TimelinePageDto>("/api/v1/timeline/events");

        page!.Items.Select(i => i.Sequence).Should().Equal(1, 2, 3, 4);
        page.Items.Take(3).Should().OnlyContain(i => i.Payload == null);
        page.Items.Select(i => i.RecordedAt).Take(3).Should().BeInAscendingOrder();
    }
}
