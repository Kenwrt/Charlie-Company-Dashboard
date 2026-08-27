using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledReportDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledReportDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ReportType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RunAtLocalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledReportDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledReportRecipients",
                columns: table => new
                {
                    ScheduledReportDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    NotificationRecipientId = table.Column<int>(type: "integer", nullable: false),
                    SendEmail = table.Column<bool>(type: "boolean", nullable: false),
                    SendSms = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledReportRecipients", x => new { x.ScheduledReportDefinitionId, x.NotificationRecipientId });
                    table.ForeignKey(
                        name: "FK_ScheduledReportRecipients_NotificationRecipients_Notificati~",
                        column: x => x.NotificationRecipientId,
                        principalTable: "NotificationRecipients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScheduledReportRecipients_ScheduledReportDefinitions_Schedu~",
                        column: x => x.ScheduledReportDefinitionId,
                        principalTable: "ScheduledReportDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledReportRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScheduledReportDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    ScheduledLocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledReportRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledReportRuns_ScheduledReportDefinitions_ScheduledRep~",
                        column: x => x.ScheduledReportDefinitionId,
                        principalTable: "ScheduledReportDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledReportRecipients_NotificationRecipientId",
                table: "ScheduledReportRecipients",
                column: "NotificationRecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledReportRuns_ScheduledReportDefinitionId_ScheduledLo~",
                table: "ScheduledReportRuns",
                columns: new[] { "ScheduledReportDefinitionId", "ScheduledLocalDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledReportRecipients");

            migrationBuilder.DropTable(
                name: "ScheduledReportRuns");

            migrationBuilder.DropTable(
                name: "ScheduledReportDefinitions");
        }
    }
}
