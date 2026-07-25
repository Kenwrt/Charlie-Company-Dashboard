using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddHousecallProRecordCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HousecallProEstimates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LocalOperationId = table.Column<int>(type: "integer", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EstimateNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ApprovalStatus = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SourceUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HousecallProEstimates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HousecallProEstimates_LocalOperations_LocalOperationId",
                        column: x => x.LocalOperationId,
                        principalTable: "LocalOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HousecallProJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LocalOperationId = table.Column<int>(type: "integer", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    JobNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    WorkStatus = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ScheduledStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ScheduledEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SourceUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HousecallProJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HousecallProJobs_LocalOperations_LocalOperationId",
                        column: x => x.LocalOperationId,
                        principalTable: "LocalOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HousecallProEstimates_LocalOperationId_ExternalId",
                table: "HousecallProEstimates",
                columns: new[] { "LocalOperationId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HousecallProEstimates_LocalOperationId_Status",
                table: "HousecallProEstimates",
                columns: new[] { "LocalOperationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_HousecallProJobs_LocalOperationId_ExternalId",
                table: "HousecallProJobs",
                columns: new[] { "LocalOperationId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HousecallProJobs_LocalOperationId_WorkStatus",
                table: "HousecallProJobs",
                columns: new[] { "LocalOperationId", "WorkStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HousecallProEstimates");

            migrationBuilder.DropTable(
                name: "HousecallProJobs");
        }
    }
}
