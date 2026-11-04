using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorPackagingHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DescriptionSnapshot",
                table: "InvoiceLines",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackageQuantitySnapshot",
                table: "InvoiceLines",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PackagingConfirmed",
                table: "InvoiceLines",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PurchaseUnitSnapshot",
                table: "InvoiceLines",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SkuSnapshot",
                table: "InvoiceLines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VendorPackagingVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VendorProductId = table.Column<int>(type: "integer", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PurchaseUnit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PackageQuantity = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    IsConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    Evidence = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ConfirmedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorPackagingVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendorPackagingVersions_VendorProducts_VendorProductId",
                        column: x => x.VendorProductId,
                        principalTable: "VendorProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VendorPackagingVersions_VendorProductId_EffectiveDate",
                table: "VendorPackagingVersions",
                columns: new[] { "VendorProductId", "EffectiveDate" },
                unique: true);
            // Legacy values are recorded history, not newly verified supplier facts.
            migrationBuilder.Sql("""
                INSERT INTO "VendorPackagingVersions" ("VendorProductId", "EffectiveDate", "PurchaseUnit", "PackageQuantity", "IsConfirmed", "Evidence", "CreatedAt")
                SELECT "Id", DATE '1900-01-01', "PurchaseUnit", "PackageQuantity", false,
                    'Legacy catalog values; supplier packaging not verified', CURRENT_TIMESTAMP FROM "VendorProducts";
                WITH candidates AS (
                    SELECT l."Id", v."VendorSku" AS sku, source.line,
                        count(*) OVER (PARTITION BY l."Id") AS matches
                    FROM "InvoiceLines" l
                    JOIN "VendorProducts" v ON v."Id" = l."VendorProductId"
                    JOIN "SupplierInvoiceImports" i ON i."PayableInvoiceId" = l."PayableInvoiceId" AND i."Status" = 'Posted'
                    CROSS JOIN LATERAL jsonb_array_elements(i."PayloadJson"::jsonb->'lines') source(line)
                    WHERE upper(trim(source.line->>'sku')) = upper(trim(v."VendorSku"))
                        AND (source.line->>'quantity')::numeric = l."Quantity"
                        AND (source.line->>'unitPrice')::numeric = l."UnitPrice"
                        AND (source.line->>'lineTotal')::numeric = l."LineTotal"
                )
                UPDATE "InvoiceLines" l SET "SkuSnapshot" = left(c.line->>'sku',100),
                    "DescriptionSnapshot" = left(c.line->>'description',300),
                    "PurchaseUnitSnapshot" = left(c.line->>'purchaseUnit',40),
                    "PackageQuantitySnapshot" = CASE WHEN coalesce((c.line->>'packagingConfirmed')::boolean,false)
                        THEN (c.line->>'packageQuantity')::numeric ELSE NULL END,
                    "PackagingConfirmed" = coalesce((c.line->>'packagingConfirmed')::boolean,false)
                FROM candidates c WHERE c."Id" = l."Id" AND c.matches = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VendorPackagingVersions");

            migrationBuilder.DropColumn(
                name: "DescriptionSnapshot",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "PackageQuantitySnapshot",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "PackagingConfirmed",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "PurchaseUnitSnapshot",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "SkuSnapshot",
                table: "InvoiceLines");
        }
    }
}
