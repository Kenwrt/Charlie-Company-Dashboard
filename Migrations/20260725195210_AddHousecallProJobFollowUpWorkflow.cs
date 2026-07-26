using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddHousecallProJobFollowUpWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InternalStatus",
                table: "HousecallProJobs",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalStatusNote",
                table: "HousecallProJobs",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InternalStatusUpdatedAt",
                table: "HousecallProJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalStatusUpdatedBy",
                table: "HousecallProJobs",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HousecallProJobFollowUps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HousecallProJobId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    EnteredByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    EnteredByName = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    EnteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HousecallProJobFollowUps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HousecallProJobFollowUps_HousecallProJobs_HousecallProJobId",
                        column: x => x.HousecallProJobId,
                        principalTable: "HousecallProJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HousecallProJobs_LocalOperationId_InternalStatus",
                table: "HousecallProJobs",
                columns: new[] { "LocalOperationId", "InternalStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_HousecallProJobFollowUps_HousecallProJobId_EnteredAt",
                table: "HousecallProJobFollowUps",
                columns: new[] { "HousecallProJobId", "EnteredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HousecallProJobFollowUps");

            migrationBuilder.DropIndex(
                name: "IX_HousecallProJobs_LocalOperationId_InternalStatus",
                table: "HousecallProJobs");

            migrationBuilder.DropColumn(
                name: "InternalStatus",
                table: "HousecallProJobs");

            migrationBuilder.DropColumn(
                name: "InternalStatusNote",
                table: "HousecallProJobs");

            migrationBuilder.DropColumn(
                name: "InternalStatusUpdatedAt",
                table: "HousecallProJobs");

            migrationBuilder.DropColumn(
                name: "InternalStatusUpdatedBy",
                table: "HousecallProJobs");
        }
    }
}
