using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialEmailSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastMaterialEmailSentAt",
                table: "QuoteCases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastMaterialEmailSignature",
                table: "QuoteCases",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastMaterialEmailSentAt",
                table: "QuoteCases");

            migrationBuilder.DropColumn(
                name: "LastMaterialEmailSignature",
                table: "QuoteCases");
        }
    }
}
