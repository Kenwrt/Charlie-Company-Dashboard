using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddAssumptionReviewKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewKind",
                table: "QuoteTaskAnalysisReviewItems",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Warning");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewKind",
                table: "QuoteTaskAnalysisReviewItems");
        }
    }
}
