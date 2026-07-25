using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddHousecallProEstimateContactDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerAddress",
                table: "HousecallProEstimates",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerEmail",
                table: "HousecallProEstimates",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerPhone",
                table: "HousecallProEstimates",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "HousecallProEstimates",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerAddress",
                table: "HousecallProEstimates");

            migrationBuilder.DropColumn(
                name: "CustomerEmail",
                table: "HousecallProEstimates");

            migrationBuilder.DropColumn(
                name: "CustomerPhone",
                table: "HousecallProEstimates");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "HousecallProEstimates");
        }
    }
}
