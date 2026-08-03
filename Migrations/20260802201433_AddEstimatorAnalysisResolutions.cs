using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddEstimatorAnalysisResolutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEstimatorLocked",
                table: "QuoteTaskAnalysisMaterials",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "QuoteTaskAnalysisReviewItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuoteTaskAnalysisId = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ItemKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EstimatorResponse = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ResolutionAction = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    AddedVendorProductId = table.Column<int>(type: "integer", nullable: true),
                    AddedProductQuantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    AdditionalFeeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AdditionalFeeAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ResolvedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteTaskAnalysisReviewItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteTaskAnalysisReviewItems_QuoteTaskAnalyses_QuoteTaskAna~",
                        column: x => x.QuoteTaskAnalysisId,
                        principalTable: "QuoteTaskAnalyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuoteTaskAnalysisReviewItems_VendorProducts_AddedVendorProd~",
                        column: x => x.AddedVendorProductId,
                        principalTable: "VendorProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTaskAnalysisReviewItems_AddedVendorProductId",
                table: "QuoteTaskAnalysisReviewItems",
                column: "AddedVendorProductId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTaskAnalysisReviewItems_QuoteTaskAnalysisId_ItemKey",
                table: "QuoteTaskAnalysisReviewItems",
                columns: new[] { "QuoteTaskAnalysisId", "ItemKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuoteTaskAnalysisReviewItems");

            migrationBuilder.DropColumn(
                name: "IsEstimatorLocked",
                table: "QuoteTaskAnalysisMaterials");
        }
    }
}
