using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPreferredCatalogSelections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPreferred",
                table: "VendorProducts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PreferencePriority",
                table: "VendorProducts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ProductSystem",
                table: "VendorProducts",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendorProducts_IsPreferred_PreferencePriority",
                table: "VendorProducts",
                columns: new[] { "IsPreferred", "PreferencePriority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VendorProducts_IsPreferred_PreferencePriority",
                table: "VendorProducts");

            migrationBuilder.DropColumn(
                name: "IsPreferred",
                table: "VendorProducts");

            migrationBuilder.DropColumn(
                name: "PreferencePriority",
                table: "VendorProducts");

            migrationBuilder.DropColumn(
                name: "ProductSystem",
                table: "VendorProducts");
        }
    }
}
