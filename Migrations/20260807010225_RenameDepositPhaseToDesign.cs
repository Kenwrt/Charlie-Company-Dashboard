using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class RenameDepositPhaseToDesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"HousecallProJobProgress\" SET \"CurrentPhase\" = 'Design' WHERE \"CurrentPhase\" = 'Deposit';");
            migrationBuilder.Sql(
                "UPDATE \"HousecallProJobPaymentMilestones\" SET \"TriggerPhase\" = 'Design' WHERE \"TriggerPhase\" = 'Deposit';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"HousecallProJobProgress\" SET \"CurrentPhase\" = 'Deposit' WHERE \"CurrentPhase\" = 'Design';");
            migrationBuilder.Sql(
                "UPDATE \"HousecallProJobPaymentMilestones\" SET \"TriggerPhase\" = 'Deposit' WHERE \"TriggerPhase\" = 'Design';");
        }
    }
}
