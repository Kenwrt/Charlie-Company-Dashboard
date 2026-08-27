using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddManualReportRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "ScheduledReportDefinitionId",
                table: "ScheduledReportRuns",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "ReportType",
                table: "ScheduledReportRuns",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "ScheduledReportRuns" AS runs
                SET "ReportType" = definitions."ReportType"
                FROM "ScheduledReportDefinitions" AS definitions
                WHERE runs."ScheduledReportDefinitionId" = definitions."Id";
                """);

            migrationBuilder.AlterColumn<string>(
                name: "ReportType",
                table: "ScheduledReportRuns",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "daily-operations",
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    report_run RECORD;
                    definition_id INTEGER;
                BEGIN
                    FOR report_run IN
                        SELECT "Id", "ReportType"
                        FROM "ScheduledReportRuns"
                        WHERE "ScheduledReportDefinitionId" IS NULL
                    LOOP
                        INSERT INTO "ScheduledReportDefinitions"
                            ("Name", "ReportType", "TimeZoneId", "RunAtLocalTime", "IsActive", "CreatedAt", "UpdatedAt")
                        VALUES
                            ('Manual report run ' || report_run."Id", report_run."ReportType", 'America/Chicago', TIME '00:00', FALSE, NOW(), NOW())
                        RETURNING "Id" INTO definition_id;

                        UPDATE "ScheduledReportRuns"
                        SET "ScheduledReportDefinitionId" = definition_id
                        WHERE "Id" = report_run."Id";
                    END LOOP;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "ReportType",
                table: "ScheduledReportRuns");

            migrationBuilder.AlterColumn<int>(
                name: "ScheduledReportDefinitionId",
                table: "ScheduledReportRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
