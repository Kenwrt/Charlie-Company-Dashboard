using System;
using CharleyCompany.Dashboard.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260922235900_AddTaskEstimateOptions")]
public partial class AddTaskEstimateOptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Measurements", table: "QuoteProjectTasks",
            type: "character varying(4000)", maxLength: 4000, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(
            name: "OptionsJson", table: "QuoteVersions", type: "text", nullable: true);
        migrationBuilder.AddColumn<Guid>(
            name: "EstimateOptionId", table: "QuoteTaskAnalyses", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "InputSignature", table: "QuoteTaskAnalyses", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<Guid>(
            name: "EstimateOptionId", table: "QuoteProcessingJobs", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "QuoteTaskAnalysisId", table: "QuoteProcessingJobs", type: "integer", nullable: true);
        // Existing versions stay on the original workflow. No price or selection backfill.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "QuoteTaskAnalysisId", table: "QuoteProcessingJobs");
        migrationBuilder.DropColumn(name: "EstimateOptionId", table: "QuoteProcessingJobs");
        migrationBuilder.DropColumn(name: "InputSignature", table: "QuoteTaskAnalyses");
        migrationBuilder.DropColumn(name: "EstimateOptionId", table: "QuoteTaskAnalyses");
        migrationBuilder.DropColumn(name: "OptionsJson", table: "QuoteVersions");
        migrationBuilder.DropColumn(name: "Measurements", table: "QuoteProjectTasks");
    }
}
