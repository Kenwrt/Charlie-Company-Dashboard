using CharleyCompany.Dashboard.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CharleyCompany.Dashboard.Web.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261104060000_FixPackagingTableOwnership")]
public sealed class FixPackagingTableOwnership : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $$ DECLARE catalog_owner text; sequence_name text;
        BEGIN
            SELECT pg_get_userbyid(relowner) INTO catalog_owner FROM pg_class
                WHERE oid = '"VendorProducts"'::regclass;
            sequence_name := pg_get_serial_sequence('"VendorPackagingVersions"','Id');
            EXECUTE format('ALTER TABLE "VendorPackagingVersions" OWNER TO %I', catalog_owner);
            IF sequence_name IS NOT NULL THEN
                EXECUTE format('ALTER SEQUENCE %s OWNER TO %I', sequence_name, catalog_owner);
            END IF;
        END $$;
        """);
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
