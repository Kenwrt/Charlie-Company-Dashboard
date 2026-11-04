using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierReconciliationRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupplierReconciliationRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SupplyVendorId = table.Column<int>(type: "integer", nullable: false),
                    LocalOperationId = table.Column<int>(type: "integer", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceJson = table.Column<string>(type: "text", nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierReconciliationRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReconciliationRuns_SnapshotHash",
                table: "SupplierReconciliationRuns",
                column: "SnapshotHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReconciliationRuns_SupplyVendorId_LocalOperationId_~",
                table: "SupplierReconciliationRuns",
                columns: new[] { "SupplyVendorId", "LocalOperationId", "CapturedAt" });
            // The migration operator may differ from the application's existing catalog owner.
            migrationBuilder.Sql("""
                DO $$ DECLARE catalog_owner text; sequence_name text;
                BEGIN
                    SELECT pg_get_userbyid(relowner) INTO catalog_owner FROM pg_class
                        WHERE oid = '"VendorProducts"'::regclass;
                    sequence_name := pg_get_serial_sequence('"SupplierReconciliationRuns"','Id');
                    EXECUTE format('ALTER TABLE "SupplierReconciliationRuns" OWNER TO %I', catalog_owner);
                    IF sequence_name IS NOT NULL THEN
                        EXECUTE format('ALTER SEQUENCE %s OWNER TO %I', sequence_name, catalog_owner);
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplierReconciliationRuns");
        }
    }
}
