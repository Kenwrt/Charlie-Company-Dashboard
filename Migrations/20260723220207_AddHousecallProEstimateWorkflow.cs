using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddHousecallProEstimateWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedByName",
                table: "HousecallProEstimates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalStatus",
                table: "HousecallProEstimates",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InternalStatusNote",
                table: "HousecallProEstimates",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InternalStatusUpdatedAt",
                table: "HousecallProEstimates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalStatusUpdatedBy",
                table: "HousecallProEstimates",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HousecallProEstimateCommunications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HousecallProEstimateId = table.Column<int>(type: "integer", nullable: false),
                    CommunicationType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Direction = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    EnteredByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    EnteredByName = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    EnteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HousecallProEstimateCommunications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HousecallProEstimateCommunications_HousecallProEstimates_Ho~",
                        column: x => x.HousecallProEstimateId,
                        principalTable: "HousecallProEstimates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HousecallProEstimateCommunications_HousecallProEstimateId_E~",
                table: "HousecallProEstimateCommunications",
                columns: new[] { "HousecallProEstimateId", "EnteredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HousecallProEstimateCommunications");

            migrationBuilder.DropColumn(
                name: "CreatedByName",
                table: "HousecallProEstimates");

            migrationBuilder.DropColumn(
                name: "InternalStatus",
                table: "HousecallProEstimates");

            migrationBuilder.DropColumn(
                name: "InternalStatusNote",
                table: "HousecallProEstimates");

            migrationBuilder.DropColumn(
                name: "InternalStatusUpdatedAt",
                table: "HousecallProEstimates");

            migrationBuilder.DropColumn(
                name: "InternalStatusUpdatedBy",
                table: "HousecallProEstimates");
        }
    }
}
