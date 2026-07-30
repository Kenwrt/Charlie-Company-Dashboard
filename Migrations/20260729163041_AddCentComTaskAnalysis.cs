using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCentComTaskAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuoteTaskAnalyses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuoteProjectTaskId = table.Column<int>(type: "integer", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SubmittedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ModelVersion = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Assumptions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    QuestionsAndWarnings = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    DeliveryAllowance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TaxAllowance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    OtherAllowance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteTaskAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteTaskAnalyses_QuoteProjectTasks_QuoteProjectTaskId",
                        column: x => x.QuoteProjectTaskId,
                        principalTable: "QuoteProjectTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuoteTaskAnalysisMaterials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuoteTaskAnalysisId = table.Column<int>(type: "integer", nullable: false),
                    VendorProductId = table.Column<int>(type: "integer", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    VendorSku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    WastePercent = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    MatchConfidence = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SourceReference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SourcePriceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsUnmatched = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteTaskAnalysisMaterials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteTaskAnalysisMaterials_QuoteTaskAnalyses_QuoteTaskAnaly~",
                        column: x => x.QuoteTaskAnalysisId,
                        principalTable: "QuoteTaskAnalyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuoteTaskAnalysisMaterials_VendorProducts_VendorProductId",
                        column: x => x.VendorProductId,
                        principalTable: "VendorProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTaskAnalyses_QuoteProjectTaskId_RevisionNumber",
                table: "QuoteTaskAnalyses",
                columns: new[] { "QuoteProjectTaskId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTaskAnalysisMaterials_QuoteTaskAnalysisId_SortOrder",
                table: "QuoteTaskAnalysisMaterials",
                columns: new[] { "QuoteTaskAnalysisId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTaskAnalysisMaterials_VendorProductId",
                table: "QuoteTaskAnalysisMaterials",
                column: "VendorProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuoteTaskAnalysisMaterials");

            migrationBuilder.DropTable(
                name: "QuoteTaskAnalyses");
        }
    }
}
