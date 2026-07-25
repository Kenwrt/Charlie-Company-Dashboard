using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddHousecallProEstimateFollowUpHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HousecallProEstimateFollowUps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HousecallProEstimateId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    EnteredByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    EnteredByName = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    EnteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HousecallProEstimateFollowUps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HousecallProEstimateFollowUps_HousecallProEstimates_Houseca~",
                        column: x => x.HousecallProEstimateId,
                        principalTable: "HousecallProEstimates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HousecallProEstimateFollowUps_HousecallProEstimateId_Entere~",
                table: "HousecallProEstimateFollowUps",
                columns: new[] { "HousecallProEstimateId", "EnteredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HousecallProEstimateFollowUps");
        }
    }
}
