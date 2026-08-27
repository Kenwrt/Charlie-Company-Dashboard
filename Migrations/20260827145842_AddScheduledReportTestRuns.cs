using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledReportTestRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScheduledReportRuns_ScheduledReportDefinitionId_ScheduledLo~",
                table: "ScheduledReportRuns");

            migrationBuilder.AddColumn<bool>(
                name: "IsTest",
                table: "ScheduledReportRuns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledReportRuns_ScheduledReportDefinitionId_ScheduledLo~",
                table: "ScheduledReportRuns",
                columns: new[] { "ScheduledReportDefinitionId", "ScheduledLocalDate" },
                unique: true,
                filter: "\"IsTest\" = FALSE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScheduledReportRuns_ScheduledReportDefinitionId_ScheduledLo~",
                table: "ScheduledReportRuns");

            migrationBuilder.DropColumn(
                name: "IsTest",
                table: "ScheduledReportRuns");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledReportRuns_ScheduledReportDefinitionId_ScheduledLo~",
                table: "ScheduledReportRuns",
                columns: new[] { "ScheduledReportDefinitionId", "ScheduledLocalDate" },
                unique: true);
        }
    }
}
