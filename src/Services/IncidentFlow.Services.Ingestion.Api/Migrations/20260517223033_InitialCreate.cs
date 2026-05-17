using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IncidentFlow.Services.Ingestion.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Signals",
                columns: table => new
                {
                    SignalId = table.Column<Guid>(type: "uuid", nullable: false),
                    SignalType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ServiceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Environment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Region = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TraceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DeploymentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Signals", x => x.SignalId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Signals_IdempotencyKey",
                table: "Signals",
                column: "IdempotencyKey",
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Signals_ServiceName_Environment_OccurredAt",
                table: "Signals",
                columns: new[] { "ServiceName", "Environment", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Signals");
        }
    }
}
