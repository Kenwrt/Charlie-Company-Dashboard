using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddVersionedCostingPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ContingencyPercentOverride",
                table: "QuoteProjectTasks",
                type: "numeric(8,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CrewSizeOverride",
                table: "QuoteProjectTasks",
                type: "numeric(8,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DailyCrewCostOverride",
                table: "QuoteProjectTasks",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedDays",
                table: "QuoteProjectTasks",
                type: "numeric(8,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetMarginPercentOverride",
                table: "QuoteProjectTasks",
                type: "numeric(8,4)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CostingPolicyVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LocalOperationId = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultDailyCrewCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DefaultCrewSize = table.Column<decimal>(type: "numeric(8,2)", nullable: false),
                    DefaultContingencyPercent = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    DefaultTargetMarginPercent = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    GeneralOverheadFixed = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    GeneralOverheadPerProjectDay = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    GeneralOverheadPercent = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostingPolicyVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CostingPolicyVersions_LocalOperations_LocalOperationId",
                        column: x => x.LocalOperationId,
                        principalTable: "LocalOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CostingPolicyRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CostingPolicyVersionId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TaskType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CalculationMethod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostingPolicyRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CostingPolicyRules_CostingPolicyVersions_CostingPolicyVersi~",
                        column: x => x.CostingPolicyVersionId,
                        principalTable: "CostingPolicyVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuoteCostSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuoteVersionId = table.Column<int>(type: "integer", nullable: false),
                    CostingPolicyVersionId = table.Column<int>(type: "integer", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    PricedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PricedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    DirectCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ProjectOverhead = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Contingency = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TotalCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TargetMarginPercent = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    SuggestedCustomerPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PriceAdjustment = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AdjustmentReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteCostSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteCostSnapshots_CostingPolicyVersions_CostingPolicyVersi~",
                        column: x => x.CostingPolicyVersionId,
                        principalTable: "CostingPolicyVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuoteCostSnapshots_QuoteVersions_QuoteVersionId",
                        column: x => x.QuoteVersionId,
                        principalTable: "QuoteVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuoteTaskCostSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuoteCostSnapshotId = table.Column<int>(type: "integer", nullable: false),
                    QuoteProjectTaskId = table.Column<int>(type: "integer", nullable: false),
                    MaterialCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    EstimatedDays = table.Column<decimal>(type: "numeric(8,2)", nullable: false),
                    CrewSize = table.Column<decimal>(type: "numeric(8,2)", nullable: false),
                    DailyCrewCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    LaborCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TaskOverhead = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Contingency = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TotalCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AppliedRules = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteTaskCostSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteTaskCostSnapshots_QuoteCostSnapshots_QuoteCostSnapshot~",
                        column: x => x.QuoteCostSnapshotId,
                        principalTable: "QuoteCostSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuoteTaskCostSnapshots_QuoteProjectTasks_QuoteProjectTaskId",
                        column: x => x.QuoteProjectTaskId,
                        principalTable: "QuoteProjectTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CostingPolicyRules_CostingPolicyVersionId",
                table: "CostingPolicyRules",
                column: "CostingPolicyVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CostingPolicyVersions_LocalOperationId_Name_RevisionNumber",
                table: "CostingPolicyVersions",
                columns: new[] { "LocalOperationId", "Name", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteCostSnapshots_CostingPolicyVersionId",
                table: "QuoteCostSnapshots",
                column: "CostingPolicyVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteCostSnapshots_QuoteVersionId_RevisionNumber",
                table: "QuoteCostSnapshots",
                columns: new[] { "QuoteVersionId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTaskCostSnapshots_QuoteCostSnapshotId",
                table: "QuoteTaskCostSnapshots",
                column: "QuoteCostSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTaskCostSnapshots_QuoteProjectTaskId",
                table: "QuoteTaskCostSnapshots",
                column: "QuoteProjectTaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CostingPolicyRules");

            migrationBuilder.DropTable(
                name: "QuoteTaskCostSnapshots");

            migrationBuilder.DropTable(
                name: "QuoteCostSnapshots");

            migrationBuilder.DropTable(
                name: "CostingPolicyVersions");

            migrationBuilder.DropColumn(
                name: "ContingencyPercentOverride",
                table: "QuoteProjectTasks");

            migrationBuilder.DropColumn(
                name: "CrewSizeOverride",
                table: "QuoteProjectTasks");

            migrationBuilder.DropColumn(
                name: "DailyCrewCostOverride",
                table: "QuoteProjectTasks");

            migrationBuilder.DropColumn(
                name: "EstimatedDays",
                table: "QuoteProjectTasks");

            migrationBuilder.DropColumn(
                name: "TargetMarginPercentOverride",
                table: "QuoteProjectTasks");
        }
    }
}
