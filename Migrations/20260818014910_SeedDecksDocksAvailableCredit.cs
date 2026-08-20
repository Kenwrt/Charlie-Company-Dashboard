using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class SeedDecksDocksAvailableCredit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "VendorCredits" ("SupplyVendorId", "LocalOperationId", "Reference", "CreditDate", "OriginalAmount", "AvailableAmount", "Notes", "CreatedAt")
                SELECT vendor."Id", operation."Id", 'SUPPLI-AVAILABLE-CREDIT-20260817', DATE '2026-08-17', 179.80, 179.80,
                       'Available credit balance reported in the Decks & Docks Suppli portal on 08/17/2026.', CURRENT_TIMESTAMP
                FROM "SupplyVendors" vendor
                CROSS JOIN "LocalOperations" operation
                WHERE vendor."Name" = 'Decks & Docks'
                  AND operation."Name" = 'Charlie Company Nashville'
                ON CONFLICT ("SupplyVendorId", "LocalOperationId", "Reference") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "VendorCredits"
                WHERE "Reference" = 'SUPPLI-AVAILABLE-CREDIT-20260817';
                """);
        }
    }
}
