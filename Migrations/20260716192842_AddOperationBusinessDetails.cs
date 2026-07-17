using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationBusinessDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                table: "LocalOperations",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                table: "LocalOperations",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "LocalOperations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "LocalOperations",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LegalName",
                table: "LocalOperations",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "LocalOperations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SalesTaxAccountNumber",
                table: "LocalOperations",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StateOrProvince",
                table: "LocalOperations",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxClassification",
                table: "LocalOperations",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxIdentificationNumber",
                table: "LocalOperations",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddressLine1",
                table: "LocalOperations");

            migrationBuilder.DropColumn(
                name: "AddressLine2",
                table: "LocalOperations");

            migrationBuilder.DropColumn(
                name: "City",
                table: "LocalOperations");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "LocalOperations");

            migrationBuilder.DropColumn(
                name: "LegalName",
                table: "LocalOperations");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "LocalOperations");

            migrationBuilder.DropColumn(
                name: "SalesTaxAccountNumber",
                table: "LocalOperations");

            migrationBuilder.DropColumn(
                name: "StateOrProvince",
                table: "LocalOperations");

            migrationBuilder.DropColumn(
                name: "TaxClassification",
                table: "LocalOperations");

            migrationBuilder.DropColumn(
                name: "TaxIdentificationNumber",
                table: "LocalOperations");
        }
    }
}
