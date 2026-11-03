using CharleyCompany.Dashboard.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261102130000_AddSupplierInvoiceAssignments")]
public partial class AddSupplierInvoiceAssignments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "PayingEntityCode", table: "PayableInvoices",
            type: "character varying(80)", maxLength: 80, nullable: true);
        migrationBuilder.AddColumn<string>(name: "YourReference", table: "PayableInvoices",
            type: "character varying(500)", maxLength: 500, nullable: true);
        // Preserve existing AP balances, location assignments and unknown payer history.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PayingEntityCode", table: "PayableInvoices");
        migrationBuilder.DropColumn(name: "YourReference", table: "PayableInvoices");
    }
}
