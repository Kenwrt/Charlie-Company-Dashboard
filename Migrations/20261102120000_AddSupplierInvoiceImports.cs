using CharleyCompany.Dashboard.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261102120000_AddSupplierInvoiceImports")]
public sealed partial class AddSupplierInvoiceImports : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "SupplierInvoiceImports", columns: table => new
        {
            Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            PriceImportDocumentId = table.Column<int>(type: "integer", nullable: false),
            PayableInvoiceId = table.Column<int>(type: "integer", nullable: true),
            Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
            CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
            PayloadJson = table.Column<string>(type: "text", nullable: false),
            OriginalPayloadJson = table.Column<string>(type: "text", nullable: false),
            ReviewReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
            ReviewedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
            CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_SupplierInvoiceImports", x => x.Id);
            table.ForeignKey("FK_SupplierImports_Documents", x => x.PriceImportDocumentId, "PriceImportDocuments", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_SupplierImports_AP", x => x.PayableInvoiceId, "PayableInvoices", "Id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_SupplierInvoiceImports_PriceImportDocumentId", "SupplierInvoiceImports", "PriceImportDocumentId", unique: true);
        migrationBuilder.CreateIndex("IX_SupplierInvoiceImports_PayableInvoiceId", "SupplierInvoiceImports", "PayableInvoiceId");
        migrationBuilder.CreateIndex("IX_SupplierInvoiceImports_Status_Id", "SupplierInvoiceImports", new[] { "Status", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("SupplierInvoiceImports");
}
