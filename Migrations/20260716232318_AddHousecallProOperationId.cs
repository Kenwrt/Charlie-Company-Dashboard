using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddHousecallProOperationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HousecallProLocationId",
                table: "LocalOperations",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocalOperations_HousecallProLocationId",
                table: "LocalOperations",
                column: "HousecallProLocationId",
                unique: true,
                filter: "\"HousecallProLocationId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LocalOperations_HousecallProLocationId",
                table: "LocalOperations");

            migrationBuilder.DropColumn(
                name: "HousecallProLocationId",
                table: "LocalOperations");
        }
    }
}
