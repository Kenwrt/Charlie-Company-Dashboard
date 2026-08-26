using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class CompleteSharedTextMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "SmsConsentEvents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "SmsAuthorityConfirmed",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SmsMarketingConsent",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SmsMarketingConsentAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "SmsConsentEvents");

            migrationBuilder.DropColumn(
                name: "SmsAuthorityConfirmed",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SmsMarketingConsent",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SmsMarketingConsentAt",
                table: "AspNetUsers");
        }
    }
}
