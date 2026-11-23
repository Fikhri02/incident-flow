using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IncidentFlow.Services.Timeline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEventSequenceAndPayload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Payload",
                table: "Entries",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Sequence",
                table: "Entries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            // The identity numbers existing rows in storage order; renumber them in the order they were
            // recorded, then move the identity past them so new entries follow.
            migrationBuilder.Sql(
                """
                UPDATE "Entries" AS e
                SET "Sequence" = ordered.n
                FROM (SELECT "EntryId", row_number() OVER (ORDER BY "RecordedAt", "EntryId") AS n FROM "Entries") AS ordered
                WHERE e."EntryId" = ordered."EntryId";

                SELECT setval(pg_get_serial_sequence('"Entries"', 'Sequence'),
                              COALESCE((SELECT max("Sequence") FROM "Entries"), 0) + 1, false);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Entries_Sequence",
                table: "Entries",
                column: "Sequence",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Entries_Sequence",
                table: "Entries");

            migrationBuilder.DropColumn(
                name: "Payload",
                table: "Entries");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "Entries");
        }
    }
}
