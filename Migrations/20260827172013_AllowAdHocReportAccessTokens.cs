using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AllowAdHocReportAccessTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "NotificationRecipientId",
                table: "ScheduledReportAccessTokens",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH retained_recipient AS (
                    INSERT INTO "NotificationRecipients"
                        ("DisplayName", "EnableEmail", "EnableSms", "EnableIMessage", "NotifyOnQuoteEvents", "NotifyOnExpenseEvents", "IsActive", "CreatedAt")
                    SELECT 'Ad hoc report links retained after rollback', FALSE, FALSE, FALSE, FALSE, FALSE, FALSE, NOW()
                    WHERE EXISTS (SELECT 1 FROM "ScheduledReportAccessTokens" WHERE "NotificationRecipientId" IS NULL)
                    RETURNING "Id"
                )
                UPDATE "ScheduledReportAccessTokens"
                SET "NotificationRecipientId" = (SELECT "Id" FROM retained_recipient)
                WHERE "NotificationRecipientId" IS NULL;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "NotificationRecipientId",
                table: "ScheduledReportAccessTokens",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
