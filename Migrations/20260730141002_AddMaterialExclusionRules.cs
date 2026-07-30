using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialExclusionRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaterialExclusionRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MatchPhrase = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TaskType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialExclusionRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuoteTaskAnalysisExclusions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuoteTaskAnalysisId = table.Column<int>(type: "integer", nullable: false),
                    MaterialExclusionRuleId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ExcludedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteTaskAnalysisExclusions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteTaskAnalysisExclusions_MaterialExclusionRules_Material~",
                        column: x => x.MaterialExclusionRuleId,
                        principalTable: "MaterialExclusionRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuoteTaskAnalysisExclusions_QuoteTaskAnalyses_QuoteTaskAnal~",
                        column: x => x.QuoteTaskAnalysisId,
                        principalTable: "QuoteTaskAnalyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialExclusionRules_MatchPhrase_TaskType",
                table: "MaterialExclusionRules",
                columns: new[] { "MatchPhrase", "TaskType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTaskAnalysisExclusions_MaterialExclusionRuleId",
                table: "QuoteTaskAnalysisExclusions",
                column: "MaterialExclusionRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTaskAnalysisExclusions_QuoteTaskAnalysisId",
                table: "QuoteTaskAnalysisExclusions",
                column: "QuoteTaskAnalysisId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuoteTaskAnalysisExclusions");

            migrationBuilder.DropTable(
                name: "MaterialExclusionRules");
        }
    }
}
