using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddDatabaseBackedFinanceSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinanceProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LocalOperationId = table.Column<int>(type: "integer", nullable: false),
                    ReportingPeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    ReportingPeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    AsOfDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReconciledCashBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    MinimumOperatingReserveTarget = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AccountingProfit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ApPolicyLimitDays = table.Column<int>(type: "integer", nullable: false),
                    CashSource = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    AccountingProfitSource = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinanceProfiles_LocalOperations_LocalOperationId",
                        column: x => x.LocalOperationId,
                        principalTable: "LocalOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinanceDebts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FinanceProfileId = table.Column<int>(type: "integer", nullable: false),
                    Creditor = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    DebtType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CurrentBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    InterestRatePercent = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    MonthlyPayment = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    NextPaymentDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Source = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceDebts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinanceDebts_FinanceProfiles_FinanceProfileId",
                        column: x => x.FinanceProfileId,
                        principalTable: "FinanceProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinanceOwnerAdjustments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FinanceProfileId = table.Column<int>(type: "integer", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Owner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Payee = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ReclassAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Source = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceOwnerAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinanceOwnerAdjustments_FinanceProfiles_FinanceProfileId",
                        column: x => x.FinanceProfileId,
                        principalTable: "FinanceProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinanceReadinessControls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FinanceProfileId = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Test = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CurrentResult = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Threshold = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Owner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    TargetDate = table.Column<DateOnly>(type: "date", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceReadinessControls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinanceReadinessControls_FinanceProfiles_FinanceProfileId",
                        column: x => x.FinanceProfileId,
                        principalTable: "FinanceProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinanceScheduledCashUses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FinanceProfileId = table.Column<int>(type: "integer", nullable: false),
                    ExpectedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Source = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceScheduledCashUses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinanceScheduledCashUses_FinanceProfiles_FinanceProfileId",
                        column: x => x.FinanceProfileId,
                        principalTable: "FinanceProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceDebts_FinanceProfileId_IsActive",
                table: "FinanceDebts",
                columns: new[] { "FinanceProfileId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceOwnerAdjustments_FinanceProfileId_Status",
                table: "FinanceOwnerAdjustments",
                columns: new[] { "FinanceProfileId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceProfiles_LocalOperationId",
                table: "FinanceProfiles",
                column: "LocalOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceReadinessControls_FinanceProfileId_Category",
                table: "FinanceReadinessControls",
                columns: new[] { "FinanceProfileId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceScheduledCashUses_FinanceProfileId_ExpectedDate_IsAc~",
                table: "FinanceScheduledCashUses",
                columns: new[] { "FinanceProfileId", "ExpectedDate", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinanceDebts");

            migrationBuilder.DropTable(
                name: "FinanceOwnerAdjustments");

            migrationBuilder.DropTable(
                name: "FinanceReadinessControls");

            migrationBuilder.DropTable(
                name: "FinanceScheduledCashUses");

            migrationBuilder.DropTable(
                name: "FinanceProfiles");
        }
    }
}
