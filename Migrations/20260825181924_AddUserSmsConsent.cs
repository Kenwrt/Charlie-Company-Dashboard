using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSmsConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SmsConsentDisclosureVersion",
                table: "AspNetUsers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SmsConsentGranted",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SmsConsentGrantedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmsConsentPhoneNumber",
                table: "AspNetUsers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SmsConsentRevokedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SmsConsentEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DisclosureVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisclosureText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsConsentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmsConsentEvents_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmsConsentEvents_UserId_OccurredAt",
                table: "SmsConsentEvents",
                columns: new[] { "UserId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmsConsentEvents");

            migrationBuilder.DropColumn(
                name: "SmsConsentDisclosureVersion",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SmsConsentGranted",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SmsConsentGrantedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SmsConsentPhoneNumber",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SmsConsentRevokedAt",
                table: "AspNetUsers");
        }
    }
}
