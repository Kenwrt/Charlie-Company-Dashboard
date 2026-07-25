using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class ExpandHousecallProJobSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Address",
                table: "HousecallProJobs",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByName",
                table: "HousecallProJobs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerEmail",
                table: "HousecallProJobs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerPhone",
                table: "HousecallProJobs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "JobPrice",
                table: "HousecallProJobs",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OutstandingBalance",
                table: "HousecallProJobs",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedByName",
                table: "HousecallProJobs");

            migrationBuilder.DropColumn(
                name: "CustomerEmail",
                table: "HousecallProJobs");

            migrationBuilder.DropColumn(
                name: "CustomerPhone",
                table: "HousecallProJobs");

            migrationBuilder.DropColumn(
                name: "JobPrice",
                table: "HousecallProJobs");

            migrationBuilder.DropColumn(
                name: "OutstandingBalance",
                table: "HousecallProJobs");

            migrationBuilder.AlterColumn<string>(
                name: "Address",
                table: "HousecallProJobs",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(400)",
                oldMaxLength: 400,
                oldNullable: true);
        }
    }
}
