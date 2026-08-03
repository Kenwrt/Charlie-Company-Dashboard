using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class EnableExistingAdminAuditEmailPreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers" AS users
                SET "AdminAuditEmail" = TRUE
                WHERE EXISTS (
                    SELECT 1
                    FROM "AspNetUserRoles" AS user_roles
                    INNER JOIN "AspNetRoles" AS roles ON roles."Id" = user_roles."RoleId"
                    WHERE user_roles."UserId" = users."Id"
                      AND roles."Name" = 'Administrator'
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers" AS users
                SET "AdminAuditEmail" = FALSE
                WHERE EXISTS (
                    SELECT 1
                    FROM "AspNetUserRoles" AS user_roles
                    INNER JOIN "AspNetRoles" AS roles ON roles."Id" = user_roles."RoleId"
                    WHERE user_roles."UserId" = users."Id"
                      AND roles."Name" = 'Administrator'
                );
                """);
        }
    }
}
