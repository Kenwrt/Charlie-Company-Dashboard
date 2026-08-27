using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledReportAccessTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledReportAccessTokens",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScheduledReportRunId = table.Column<long>(type: "bigint", nullable: false),
                    NotificationRecipientId = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAccessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledReportAccessTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledReportAccessTokens_NotificationRecipients_Notifica~",
                        column: x => x.NotificationRecipientId,
                        principalTable: "NotificationRecipients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScheduledReportAccessTokens_ScheduledReportRuns_ScheduledRe~",
                        column: x => x.ScheduledReportRunId,
                        principalTable: "ScheduledReportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledReportAccessTokens_ExpiresAt",
                table: "ScheduledReportAccessTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledReportAccessTokens_NotificationRecipientId",
                table: "ScheduledReportAccessTokens",
                column: "NotificationRecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledReportAccessTokens_ScheduledReportRunId",
                table: "ScheduledReportAccessTokens",
                column: "ScheduledReportRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledReportAccessTokens_TokenHash",
                table: "ScheduledReportAccessTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledReportAccessTokens");
        }
    }
}
