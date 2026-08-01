using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialCatalogFallbackReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRemoved",
                table: "QuoteTaskAnalysisMaterials",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MatchKind",
                table: "QuoteTaskAnalysisMaterials",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Catalog");

            migrationBuilder.AddColumn<string>(
                name: "OriginalDescription",
                table: "QuoteTaskAnalysisMaterials",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewDecision",
                table: "QuoteTaskAnalysisMaterials",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Accepted");

            migrationBuilder.Sql("""
                UPDATE "QuoteTaskAnalysisMaterials"
                SET "OriginalDescription" = "Description",
                    "MatchKind" = CASE WHEN "IsUnmatched" THEN 'Unresolved' ELSE 'Catalog' END,
                    "ReviewDecision" = CASE WHEN "IsUnmatched" THEN 'Pending' ELSE 'Accepted' END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRemoved",
                table: "QuoteTaskAnalysisMaterials");

            migrationBuilder.DropColumn(
                name: "MatchKind",
                table: "QuoteTaskAnalysisMaterials");

            migrationBuilder.DropColumn(
                name: "OriginalDescription",
                table: "QuoteTaskAnalysisMaterials");

            migrationBuilder.DropColumn(
                name: "ReviewDecision",
                table: "QuoteTaskAnalysisMaterials");
        }
    }
}
