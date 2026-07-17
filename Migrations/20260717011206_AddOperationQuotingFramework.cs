using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationQuotingFramework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuoteCases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LocalOperationId = table.Column<int>(type: "integer", nullable: false),
                    HousecallProQuoteId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    HousecallProJobId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    HousecallProCustomerId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    CompanyCamProjectId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    CustomerName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    CustomerEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    WorkDescription = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AssignedUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteCases_AspNetUsers_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_QuoteCases_LocalOperations_LocalOperationId",
                        column: x => x.LocalOperationId,
                        principalTable: "LocalOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuotePricingRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LocalOperationId = table.Column<int>(type: "integer", nullable: false),
                    DefaultLaborRate = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    DefaultMarkupPercent = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    DefaultWastePercent = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    DefaultTaxRate = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    MinimumGrossMarginPercent = table.Column<decimal>(type: "numeric(8,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuotePricingRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuotePricingRules_LocalOperations_LocalOperationId",
                        column: x => x.LocalOperationId,
                        principalTable: "LocalOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuoteAuditEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuoteCaseId = table.Column<int>(type: "integer", nullable: false),
                    PreviousStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    NewStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Explanation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteAuditEvents_QuoteCases_QuoteCaseId",
                        column: x => x.QuoteCaseId,
                        principalTable: "QuoteCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuoteProcessingJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuoteCaseId = table.Column<int>(type: "integer", nullable: false),
                    JobType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteProcessingJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteProcessingJobs_QuoteCases_QuoteCaseId",
                        column: x => x.QuoteCaseId,
                        principalTable: "QuoteCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuoteVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuoteCaseId = table.Column<int>(type: "integer", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TaxRate = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CustomerNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ApprovedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteVersions_QuoteCases_QuoteCaseId",
                        column: x => x.QuoteCaseId,
                        principalTable: "QuoteCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuoteLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuoteVersionId = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    MaterialUnitCost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    LaborHours = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    LaborRate = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    EquipmentCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    WastePercent = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    MarkupPercent = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    CustomerPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteLines_QuoteVersions_QuoteVersionId",
                        column: x => x.QuoteVersionId,
                        principalTable: "QuoteVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteAuditEvents_QuoteCaseId",
                table: "QuoteAuditEvents",
                column: "QuoteCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteCases_AssignedUserId",
                table: "QuoteCases",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteCases_HousecallProQuoteId",
                table: "QuoteCases",
                column: "HousecallProQuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteCases_LocalOperationId_Status",
                table: "QuoteCases",
                columns: new[] { "LocalOperationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteLines_QuoteVersionId",
                table: "QuoteLines",
                column: "QuoteVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuotePricingRules_LocalOperationId",
                table: "QuotePricingRules",
                column: "LocalOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteProcessingJobs_QuoteCaseId",
                table: "QuoteProcessingJobs",
                column: "QuoteCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteVersions_QuoteCaseId_VersionNumber",
                table: "QuoteVersions",
                columns: new[] { "QuoteCaseId", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuoteAuditEvents");

            migrationBuilder.DropTable(
                name: "QuoteLines");

            migrationBuilder.DropTable(
                name: "QuotePricingRules");

            migrationBuilder.DropTable(
                name: "QuoteProcessingJobs");

            migrationBuilder.DropTable(
                name: "QuoteVersions");

            migrationBuilder.DropTable(
                name: "QuoteCases");
        }
    }
}
