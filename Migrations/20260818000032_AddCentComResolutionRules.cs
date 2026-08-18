using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCentComResolutionRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CentComResolutionRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RuleKind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    MatchText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ReviewStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ResolutionAction = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    EstimatorResponse = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    MaterialDecision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    VendorProductId = table.Column<int>(type: "integer", nullable: true),
                    MaterialDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MaterialUnit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    MaterialUnitCost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CentComResolutionRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CentComResolutionRules_VendorProducts_VendorProductId",
                        column: x => x.VendorProductId,
                        principalTable: "VendorProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CentComResolutionRules_TaskType_RuleKind_MatchText",
                table: "CentComResolutionRules",
                columns: new[] { "TaskType", "RuleKind", "MatchText" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CentComResolutionRules_VendorProductId",
                table: "CentComResolutionRules",
                column: "VendorProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CentComResolutionRules");
        }
    }
}
