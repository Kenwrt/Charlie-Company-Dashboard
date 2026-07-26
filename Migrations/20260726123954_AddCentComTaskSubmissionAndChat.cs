using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCentComTaskSubmissionAndChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequirementKey",
                table: "QuoteProjectTaskPhotos",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RequirementLabel",
                table: "QuoteProjectTaskPhotos",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "QuoteProjectTaskId",
                table: "QuoteProcessingJobs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CentComChatSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CentComChatSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CentComChatSessions_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CentComChatMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CentComChatSessionId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Content = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CentComChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CentComChatMessages_CentComChatSessions_CentComChatSessionId",
                        column: x => x.CentComChatSessionId,
                        principalTable: "CentComChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteProcessingJobs_QuoteProjectTaskId",
                table: "QuoteProcessingJobs",
                column: "QuoteProjectTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_CentComChatMessages_CentComChatSessionId_CreatedAt",
                table: "CentComChatMessages",
                columns: new[] { "CentComChatSessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CentComChatSessions_CreatedByUserId_UpdatedAt",
                table: "CentComChatSessions",
                columns: new[] { "CreatedByUserId", "UpdatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_QuoteProcessingJobs_QuoteProjectTasks_QuoteProjectTaskId",
                table: "QuoteProcessingJobs",
                column: "QuoteProjectTaskId",
                principalTable: "QuoteProjectTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuoteProcessingJobs_QuoteProjectTasks_QuoteProjectTaskId",
                table: "QuoteProcessingJobs");

            migrationBuilder.DropTable(
                name: "CentComChatMessages");

            migrationBuilder.DropTable(
                name: "CentComChatSessions");

            migrationBuilder.DropIndex(
                name: "IX_QuoteProcessingJobs_QuoteProjectTaskId",
                table: "QuoteProcessingJobs");

            migrationBuilder.DropColumn(
                name: "RequirementKey",
                table: "QuoteProjectTaskPhotos");

            migrationBuilder.DropColumn(
                name: "RequirementLabel",
                table: "QuoteProjectTaskPhotos");

            migrationBuilder.DropColumn(
                name: "QuoteProjectTaskId",
                table: "QuoteProcessingJobs");
        }
    }
}
