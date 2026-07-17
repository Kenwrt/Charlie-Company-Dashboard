using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddConsolidatedOperationalEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperationalEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Environment = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Machine = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Service = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Module = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    LocalOperationId = table.Column<int>(type: "integer", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    QuoteCaseId = table.Column<int>(type: "integer", nullable: true),
                    JobNumber = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Step = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExceptionSummary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    MetadataJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperationalEvents_LocalOperations_LocalOperationId",
                        column: x => x.LocalOperationId,
                        principalTable: "LocalOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalEvents_CorrelationId_Timestamp",
                table: "OperationalEvents",
                columns: new[] { "CorrelationId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalEvents_EventId",
                table: "OperationalEvents",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationalEvents_LocalOperationId_Timestamp",
                table: "OperationalEvents",
                columns: new[] { "LocalOperationId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperationalEvents");
        }
    }
}
