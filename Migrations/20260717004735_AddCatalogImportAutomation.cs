using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogImportAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogSyncJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SupplyVendorId = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogSyncJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogSyncJobs_SupplyVendors_SupplyVendorId",
                        column: x => x.SupplyVendorId,
                        principalTable: "SupplyVendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PriceApprovalRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SupplyVendorId = table.Column<int>(type: "integer", nullable: true),
                    AutoApprovePercentThreshold = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    AutoApproveDollarThreshold = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    AutoApproveTrustedApiChanges = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceApprovalRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceApprovalRules_SupplyVendors_SupplyVendorId",
                        column: x => x.SupplyVendorId,
                        principalTable: "SupplyVendors",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PriceImportDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SupplyVendorId = table.Column<int>(type: "integer", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceImportDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceImportDocuments_SupplyVendors_SupplyVendorId",
                        column: x => x.SupplyVendorId,
                        principalTable: "SupplyVendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PriceImportRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PriceImportDocumentId = table.Column<int>(type: "integer", nullable: false),
                    VendorProductId = table.Column<int>(type: "integer", nullable: true),
                    VendorSku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ProposedUnitPrice = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MatchConfidence = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    ReviewStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceImportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceImportRows_PriceImportDocuments_PriceImportDocumentId",
                        column: x => x.PriceImportDocumentId,
                        principalTable: "PriceImportDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PriceImportRows_VendorProducts_VendorProductId",
                        column: x => x.VendorProductId,
                        principalTable: "VendorProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogSyncJobs_SupplyVendorId",
                table: "CatalogSyncJobs",
                column: "SupplyVendorId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceApprovalRules_SupplyVendorId",
                table: "PriceApprovalRules",
                column: "SupplyVendorId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceImportDocuments_Sha256",
                table: "PriceImportDocuments",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceImportDocuments_SupplyVendorId",
                table: "PriceImportDocuments",
                column: "SupplyVendorId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceImportRows_PriceImportDocumentId",
                table: "PriceImportRows",
                column: "PriceImportDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceImportRows_VendorProductId",
                table: "PriceImportRows",
                column: "VendorProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogSyncJobs");

            migrationBuilder.DropTable(
                name: "PriceApprovalRules");

            migrationBuilder.DropTable(
                name: "PriceImportRows");

            migrationBuilder.DropTable(
                name: "PriceImportDocuments");
        }
    }
}
