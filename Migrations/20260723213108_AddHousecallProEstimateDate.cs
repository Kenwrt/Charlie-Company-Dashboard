using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddHousecallProEstimateDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EstimateDate",
                table: "HousecallProEstimates",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstimateDate",
                table: "HousecallProEstimates");
        }
    }
}
