using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateHousecallProEstimateStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "InternalStatus",
                table: "HousecallProEstimates",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldDefaultValue: "New");

            migrationBuilder.Sql("""
                UPDATE "HousecallProEstimates"
                SET "InternalStatus" = NULL
                WHERE "InternalStatus" = 'New';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "HousecallProEstimates"
                SET "InternalStatus" = 'New'
                WHERE "InternalStatus" IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "InternalStatus",
                table: "HousecallProEstimates",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "New",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldNullable: true);
        }
    }
}
