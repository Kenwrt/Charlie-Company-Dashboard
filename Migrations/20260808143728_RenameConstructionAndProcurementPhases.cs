using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class RenameConstructionAndProcurementPhases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"HousecallProJobProgress\" SET \"CurrentPhase\" = 'Framing Complete' WHERE \"CurrentPhase\" = 'Procurement';");
            migrationBuilder.Sql(
                "UPDATE \"HousecallProJobProgress\" SET \"CurrentPhase\" = 'Day One' WHERE \"CurrentPhase\" = 'Construction';");
            migrationBuilder.Sql(
                "UPDATE \"HousecallProJobPaymentMilestones\" SET \"TriggerPhase\" = 'Framing Complete' WHERE \"TriggerPhase\" = 'Procurement';");
            migrationBuilder.Sql(
                "UPDATE \"HousecallProJobPaymentMilestones\" SET \"TriggerPhase\" = 'Day One' WHERE \"TriggerPhase\" = 'Construction';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"HousecallProJobProgress\" SET \"CurrentPhase\" = 'Procurement' WHERE \"CurrentPhase\" = 'Framing Complete';");
            migrationBuilder.Sql(
                "UPDATE \"HousecallProJobProgress\" SET \"CurrentPhase\" = 'Construction' WHERE \"CurrentPhase\" = 'Day One';");
            migrationBuilder.Sql(
                "UPDATE \"HousecallProJobPaymentMilestones\" SET \"TriggerPhase\" = 'Procurement' WHERE \"TriggerPhase\" = 'Framing Complete';");
            migrationBuilder.Sql(
                "UPDATE \"HousecallProJobPaymentMilestones\" SET \"TriggerPhase\" = 'Construction' WHERE \"TriggerPhase\" = 'Day One';");
        }
    }
}
