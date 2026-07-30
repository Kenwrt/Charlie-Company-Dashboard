using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddLayeredCostingModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AllocatedProjectOverhead",
                table: "QuoteTaskCostSnapshots",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RequiredSupplyCost",
                table: "QuoteTaskCostSnapshots",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SuggestedCustomerPrice",
                table: "QuoteTaskCostSnapshots",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetMarginPercent",
                table: "QuoteTaskCostSnapshots",
                type: "numeric(8,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "WorkType",
                table: "QuoteProjectTasks",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryReference",
                table: "MaterialExclusionRules",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryType",
                table: "MaterialExclusionRules",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedMonthlyProductiveCrewDays",
                table: "CostingPolicyVersions",
                type: "numeric(8,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyOverheadBudget",
                table: "CostingPolicyVersions",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "CrewRateCards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CostingPolicyVersionId = table.Column<int>(type: "integer", nullable: false),
                    TaskType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WorkType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    CrewSize = table.Column<decimal>(type: "numeric(8,2)", nullable: false),
                    DailyCrewCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrewRateCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrewRateCards_CostingPolicyVersions_CostingPolicyVersionId",
                        column: x => x.CostingPolicyVersionId,
                        principalTable: "CostingPolicyVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuoteTaskSupplyCostSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuoteTaskCostSnapshotId = table.Column<int>(type: "integer", nullable: false),
                    VendorProductId = table.Column<int>(type: "integer", nullable: true),
                    KitName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    WastePercent = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    ExtendedCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteTaskSupplyCostSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteTaskSupplyCostSnapshots_QuoteTaskCostSnapshots_QuoteTa~",
                        column: x => x.QuoteTaskCostSnapshotId,
                        principalTable: "QuoteTaskCostSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuoteTaskSupplyCostSnapshots_VendorProducts_VendorProductId",
                        column: x => x.VendorProductId,
                        principalTable: "VendorProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StandardSupplyKits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CostingPolicyVersionId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TaskType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    WorkType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandardSupplyKits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StandardSupplyKits_CostingPolicyVersions_CostingPolicyVersi~",
                        column: x => x.CostingPolicyVersionId,
                        principalTable: "CostingPolicyVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskMarginRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CostingPolicyVersionId = table.Column<int>(type: "integer", nullable: false),
                    TaskType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WorkType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    TargetMarginPercent = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskMarginRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskMarginRules_CostingPolicyVersions_CostingPolicyVersionId",
                        column: x => x.CostingPolicyVersionId,
                        principalTable: "CostingPolicyVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StandardSupplyKitItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StandardSupplyKitId = table.Column<int>(type: "integer", nullable: false),
                    VendorProductId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    WastePercent = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandardSupplyKitItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StandardSupplyKitItems_StandardSupplyKits_StandardSupplyKit~",
                        column: x => x.StandardSupplyKitId,
                        principalTable: "StandardSupplyKits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StandardSupplyKitItems_VendorProducts_VendorProductId",
                        column: x => x.VendorProductId,
                        principalTable: "VendorProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrewRateCards_CostingPolicyVersionId_TaskType_WorkType",
                table: "CrewRateCards",
                columns: new[] { "CostingPolicyVersionId", "TaskType", "WorkType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTaskSupplyCostSnapshots_QuoteTaskCostSnapshotId",
                table: "QuoteTaskSupplyCostSnapshots",
                column: "QuoteTaskCostSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTaskSupplyCostSnapshots_VendorProductId",
                table: "QuoteTaskSupplyCostSnapshots",
                column: "VendorProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StandardSupplyKitItems_StandardSupplyKitId_VendorProductId",
                table: "StandardSupplyKitItems",
                columns: new[] { "StandardSupplyKitId", "VendorProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StandardSupplyKitItems_VendorProductId",
                table: "StandardSupplyKitItems",
                column: "VendorProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StandardSupplyKits_CostingPolicyVersionId_Name",
                table: "StandardSupplyKits",
                columns: new[] { "CostingPolicyVersionId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskMarginRules_CostingPolicyVersionId_TaskType_WorkType",
                table: "TaskMarginRules",
                columns: new[] { "CostingPolicyVersionId", "TaskType", "WorkType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrewRateCards");

            migrationBuilder.DropTable(
                name: "QuoteTaskSupplyCostSnapshots");

            migrationBuilder.DropTable(
                name: "StandardSupplyKitItems");

            migrationBuilder.DropTable(
                name: "TaskMarginRules");

            migrationBuilder.DropTable(
                name: "StandardSupplyKits");

            migrationBuilder.DropColumn(
                name: "AllocatedProjectOverhead",
                table: "QuoteTaskCostSnapshots");

            migrationBuilder.DropColumn(
                name: "RequiredSupplyCost",
                table: "QuoteTaskCostSnapshots");

            migrationBuilder.DropColumn(
                name: "SuggestedCustomerPrice",
                table: "QuoteTaskCostSnapshots");

            migrationBuilder.DropColumn(
                name: "TargetMarginPercent",
                table: "QuoteTaskCostSnapshots");

            migrationBuilder.DropColumn(
                name: "WorkType",
                table: "QuoteProjectTasks");

            migrationBuilder.DropColumn(
                name: "RecoveryReference",
                table: "MaterialExclusionRules");

            migrationBuilder.DropColumn(
                name: "RecoveryType",
                table: "MaterialExclusionRules");

            migrationBuilder.DropColumn(
                name: "ExpectedMonthlyProductiveCrewDays",
                table: "CostingPolicyVersions");

            migrationBuilder.DropColumn(
                name: "MonthlyOverheadBudget",
                table: "CostingPolicyVersions");
        }
    }
}
