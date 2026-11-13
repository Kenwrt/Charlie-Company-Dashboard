using CharleyCompany.Dashboard.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261113141000_AddDocumentControl")]
public partial class AddDocumentControl : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ControlledDocuments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                Archived = table.Column<bool>(type: "boolean", nullable: false),
                Revision = table.Column<int>(type: "integer", nullable: false),
                LastVersion = table.Column<int>(type: "integer", nullable: false),
                CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ControlledDocuments", row => row.Id);
            });

        migrationBuilder.CreateTable(
            name: "ControlledDocumentVersions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                Number = table.Column<int>(type: "integer", nullable: false),
                FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Notes = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                UploadedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                UploadedByName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                UploadedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                PublishedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LeaseUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ConversionAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ControlledDocumentVersions", row => row.Id);
                table.ForeignKey("FK_ControlledDocumentVersions_ControlledDocuments_DocumentId", row => row.DocumentId,
                    "ControlledDocuments", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_ControlledDocumentVersions_DocumentId_Number", "ControlledDocumentVersions",
            new[] { "DocumentId", "Number" }, unique: true);
        migrationBuilder.CreateIndex("IX_ControlledDocumentVersions_Status_LeaseUntilUtc", "ControlledDocumentVersions",
            new[] { "Status", "LeaseUntilUtc" });

        migrationBuilder.CreateTable(
            name: "ControlledDocumentEvents",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Action = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                Detail = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ControlledDocumentEvents", row => row.Id);
                table.ForeignKey("FK_ControlledDocumentEvents_ControlledDocuments_DocumentId", row => row.DocumentId,
                    "ControlledDocuments", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_ControlledDocumentEvents_DocumentId_Id", "ControlledDocumentEvents",
            new[] { "DocumentId", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("ControlledDocumentEvents");
        migrationBuilder.DropTable("ControlledDocumentVersions");
        migrationBuilder.DropTable("ControlledDocuments");
    }
}
