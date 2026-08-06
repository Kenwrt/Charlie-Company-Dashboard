using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddJobProgressRevenueRisk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HousecallProJobBlockers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HousecallProJobId = table.Column<int>(type: "integer", nullable: false),
                    BlockerType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    StartedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpectedResolutionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ResolvedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    NextAction = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NextFollowUpDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ResponsibleParty = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RevenueAtRisk = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ResolutionNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HousecallProJobBlockers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HousecallProJobBlockers_HousecallProJobs_HousecallProJobId",
                        column: x => x.HousecallProJobId,
                        principalTable: "HousecallProJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HousecallProJobPaymentMilestones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HousecallProJobId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    TriggerPhase = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpectedPaymentDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HousecallProJobPaymentMilestones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HousecallProJobPaymentMilestones_HousecallProJobs_Housecall~",
                        column: x => x.HousecallProJobId,
                        principalTable: "HousecallProJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HousecallProJobProgress",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HousecallProJobId = table.Column<int>(type: "integer", nullable: false),
                    CurrentPhase = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PhaseEnteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpectedPhaseCompletionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RevisedJobCompletionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NextAction = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NextFollowUpDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ResponsibleParty = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HousecallProJobProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HousecallProJobProgress_HousecallProJobs_HousecallProJobId",
                        column: x => x.HousecallProJobId,
                        principalTable: "HousecallProJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HousecallProJobProgressEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HousecallProJobId = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EnteredBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HousecallProJobProgressEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HousecallProJobProgressEvents_HousecallProJobs_HousecallPro~",
                        column: x => x.HousecallProJobId,
                        principalTable: "HousecallProJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HousecallProJobBlockers_HousecallProJobId_ResolvedOn",
                table: "HousecallProJobBlockers",
                columns: new[] { "HousecallProJobId", "ResolvedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_HousecallProJobPaymentMilestones_HousecallProJobId_Status",
                table: "HousecallProJobPaymentMilestones",
                columns: new[] { "HousecallProJobId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_HousecallProJobProgress_HousecallProJobId",
                table: "HousecallProJobProgress",
                column: "HousecallProJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HousecallProJobProgressEvents_HousecallProJobId_OccurredAt",
                table: "HousecallProJobProgressEvents",
                columns: new[] { "HousecallProJobId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HousecallProJobBlockers");

            migrationBuilder.DropTable(
                name: "HousecallProJobPaymentMilestones");

            migrationBuilder.DropTable(
                name: "HousecallProJobProgress");

            migrationBuilder.DropTable(
                name: "HousecallProJobProgressEvents");
        }
    }
}
