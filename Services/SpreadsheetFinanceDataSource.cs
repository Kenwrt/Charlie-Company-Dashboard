using CharleyCompany.Dashboard.Web.Models;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class SpreadsheetFinanceDataSource : IFinanceDataSource
{
    public Task<VentureFinanceDashboard> GetVentureFinanceDashboardAsync(CancellationToken cancellationToken)
    {
        var dashboards = BuildEntityData()
            .Select(BuildDashboard)
            .ToList();

        var rollup = new FinanceSummary(
            EntityName: "Charlie Company Ventures Group",
            EntitySlug: "ventures",
            KnownRevenue: dashboards.Sum(dashboard => dashboard.Summary.KnownRevenue),
            KnownAccountingProfit: dashboards.Sum(dashboard => dashboard.Summary.KnownAccountingProfit),
            OpenAccountsPayable: dashboards.Sum(dashboard => dashboard.Summary.OpenAccountsPayable),
            DebtOutstanding: dashboards.Sum(dashboard => dashboard.Summary.DebtOutstanding),
            OwnerBenefits: dashboards.Sum(dashboard => dashboard.Summary.OwnerBenefits),
            ActualEndingCash: dashboards.Sum(dashboard => dashboard.Summary.ActualEndingCash),
            CalculatedEndingCash: dashboards.Sum(dashboard => dashboard.Summary.CalculatedEndingCash),
            CashVariance: dashboards.Sum(dashboard => dashboard.Summary.CashVariance),
            ReserveSurplusOrDeficit: dashboards.Sum(dashboard => dashboard.Summary.ReserveSurplusOrDeficit),
            AdjustedEconomicProfitProxy: dashboards.Sum(dashboard => dashboard.Summary.AdjustedEconomicProfitProxy),
            OpenApAsPercentOfRevenue: Percent(dashboards.Sum(dashboard => dashboard.Summary.OpenAccountsPayable), dashboards.Sum(dashboard => dashboard.Summary.KnownRevenue)),
            DebtAsPercentOfRevenue: Percent(dashboards.Sum(dashboard => dashboard.Summary.DebtOutstanding), dashboards.Sum(dashboard => dashboard.Summary.KnownRevenue)),
            OwnerBenefitsAsPercentOfRevenue: Percent(dashboards.Sum(dashboard => dashboard.Summary.OwnerBenefits), dashboards.Sum(dashboard => dashboard.Summary.KnownRevenue)),
            MaxApDaysPastDue: dashboards.Count == 0 ? 0 : dashboards.Max(dashboard => dashboard.Summary.MaxApDaysPastDue),
            ReadinessScore: dashboards.Count == 0 ? 0 : dashboards.Average(dashboard => dashboard.Summary.ReadinessScore));

        return Task.FromResult(new VentureFinanceDashboard(rollup, dashboards));
    }

    public async Task<FinanceDashboard?> GetEntityFinanceDashboardAsync(string entitySlug, CancellationToken cancellationToken)
    {
        var venture = await GetVentureFinanceDashboardAsync(cancellationToken);

        return venture.LocalDashboards
            .FirstOrDefault(dashboard => dashboard.Summary.EntitySlug.Equals(entitySlug, StringComparison.OrdinalIgnoreCase));
    }

    private static FinanceDashboard BuildDashboard(FinanceEntityData data)
    {
        var asOfDate = data.Assumptions.ReviewDate;
        var openAp = data.AccountsPayable.Sum(item => item.OpenBalance);
        var debt = data.DebtItems.Sum(item => item.CurrentBalance);
        var ownerBenefits = data.OwnerBenefits.Sum(item => item.ReclassAmount);
        var beginningCash = data.BankTransactions.Where(item => item.TransactionType == "Beginning Cash").Sum(item => item.Amount);
        var customerCollections = data.RevenueCollections.Sum(item => item.CollectedAmount);
        var otherDeposits = data.BankTransactions
            .Where(item => item.TransactionType == "Deposit")
            .Sum(item => item.Amount) - customerCollections;
        var materialsPaid = -data.BankTransactions.Where(item => item.BookCategory == "Materials").Sum(item => item.Amount);
        var laborPaid = -data.BankTransactions.Where(item => item.BookCategory == "Labor").Sum(item => item.Amount);
        var operatingExpensesPaid = -data.BankTransactions.Where(item => item.BookCategory == "Operating Expense").Sum(item => item.Amount);
        var debtPayments = -data.BankTransactions.Where(item => item.BookCategory == "Debt").Sum(item => item.Amount);
        var taxesPaid = -data.BankTransactions.Where(item => item.BookCategory == "Taxes").Sum(item => item.Amount);
        var totalCashAvailable = beginningCash + customerCollections + otherDeposits;
        var totalCashUses = materialsPaid + laborPaid + operatingExpensesPaid + debtPayments + taxesPaid + ownerBenefits;
        var calculatedEndingCash = totalCashAvailable - totalCashUses;
        var actualEndingCash = data.CashForecast.FirstOrDefault()?.BeginningCash ?? 0m;
        var reserveSurplus = actualEndingCash - data.Assumptions.MinimumOperatingReserveTarget;
        var adjustedEconomicProfit = data.Assumptions.KnownAccountingProfit - (openAp + debt) - ownerBenefits;

        var systemReadinessChecks = new List<ReadinessCheck>
        {
            new("Liquidity", "Operating reserve surplus / deficit", reserveSurplus.ToString("C0"), ">= 0", reserveSurplus >= 0 ? "Pass" : "Fail", reserveSurplus >= 0 ? 1 : 0, "Company", DateOnly.FromDateTime(new DateTime(2026, 8, 15))),
            new("AP", "No vendors >60 days without payment plan", data.AccountsPayable.Max(item => item.DaysPastDue(asOfDate)).ToString("N0"), "<= 60 or approved plan", data.AccountsPayable.Max(item => item.DaysPastDue(asOfDate)) <= data.Assumptions.ApPolicyLimitDays ? "Pass" : "Fail", data.AccountsPayable.Max(item => item.DaysPastDue(asOfDate)) <= data.Assumptions.ApPolicyLimitDays ? 1 : 0, "Company", DateOnly.FromDateTime(new DateTime(2026, 8, 15)))
        };

        var readinessChecks = systemReadinessChecks
            .Concat(data.ManualReadinessChecks)
            .ToList();

        var readinessScore = readinessChecks.Count == 0 ? 0 : readinessChecks.Average(check => check.Score);

        var summary = new FinanceSummary(
            data.EntityName,
            data.EntitySlug,
            data.Assumptions.KnownRevenue,
            data.Assumptions.KnownAccountingProfit,
            openAp,
            debt,
            ownerBenefits,
            actualEndingCash,
            calculatedEndingCash,
            calculatedEndingCash - actualEndingCash,
            reserveSurplus,
            adjustedEconomicProfit,
            Percent(openAp, data.Assumptions.KnownRevenue),
            Percent(debt, data.Assumptions.KnownRevenue),
            Percent(ownerBenefits, data.Assumptions.KnownRevenue),
            data.AccountsPayable.Count == 0 ? 0 : data.AccountsPayable.Max(item => item.DaysPastDue(asOfDate)),
            readinessScore);

        return new FinanceDashboard(summary, data, readinessChecks);
    }

    private static IReadOnlyList<FinanceEntityData> BuildEntityData() =>
    [
        BuildEntity("Charlie Company Nashville", "nashville", 1.00m, 0m),
        BuildEntity("Charlie Company Knoxville", "knoxville", 0.72m, 6_500m),
        BuildEntity("Charlie Company Chattanooga", "chattanooga", 0.58m, 9_000m)
    ];

    private static FinanceEntityData BuildEntity(string entityName, string entitySlug, decimal scale, decimal beginningCashAdjustment)
    {
        var assumptions = new FinanceAssumptions(
            DateOnly.FromDateTime(new DateTime(2026, 1, 1)),
            DateOnly.FromDateTime(new DateTime(2026, 6, 30)),
            Round(230_000m * scale),
            Round(60_000m * scale),
            30_000m,
            60,
            2_500m,
            0.0925m,
            DateOnly.FromDateTime(new DateTime(2026, 7, 9)));

        var beginningCash = Round((15_000m * scale) + beginningCashAdjustment);

        var bankTransactions = new List<BankTransaction>
        {
            new(assumptions.StartDate, "Operating", "Beginning Balance", "Beginning Cash", beginningCash, "Cash", "Business", "Company", false, false, "Bank stmt"),
            new(DateOnly.FromDateTime(new DateTime(2026, 1, 5)), "Operating", "Customer deposit", "Deposit", Round(12_000m * scale), "Revenue", "Business", "Company", true, false, "Housecall Pro / bank stmt"),
            new(DateOnly.FromDateTime(new DateTime(2026, 1, 6)), "Operating", "Vendor payment", "Withdrawal", -Round(4_500m * scale), "Materials", "Business", "Company", false, true, "Bank stmt"),
            new(DateOnly.FromDateTime(new DateTime(2026, 1, 7)), "Operating", "Owner personal rent", "Withdrawal", -Round(2_500m * scale), "Owner Benefit", "Personal", "Brian", false, false, "Bank stmt")
        };

        var revenueCollections = new List<RevenueCollection>
        {
            new(DateOnly.FromDateTime(new DateTime(2026, 1, 5)), "Sample Deck Job", "INV-001", "HCP-001", Round(12_000m * scale), Round(12_000m * scale), "Card", true, "Housecall Pro")
        };

        var ap = new List<AccountsPayableItem>
        {
            new("ABC Lumber", "Tier 1 - Critical", "1001", DateOnly.FromDateTime(new DateTime(2026, 2, 1)), DateOnly.FromDateTime(new DateTime(2026, 2, 15)), Round(8_000m * scale), 0, "Open", Round(500m * scale), "Critical supplier", "Vendor stmt"),
            new("Sherwin Williams", "Tier 2 - Important", "1002", DateOnly.FromDateTime(new DateTime(2026, 3, 1)), DateOnly.FromDateTime(new DateTime(2026, 3, 15)), Round(3_500m * scale), 0, "Open", Round(250m * scale), "Paint supplier", "Vendor stmt"),
            new("Home Depot", "Tier 2 - Important", "1003", DateOnly.FromDateTime(new DateTime(2026, 4, 1)), DateOnly.FromDateTime(new DateTime(2026, 4, 15)), Round(2_100m * scale), 0, "Open", Round(150m * scale), "Materials account", "Vendor stmt")
        };

        var debt = new List<DebtItem>
        {
            new("Friend/Family Notes", "Informal Note", Round(35_000m * scale), Round(35_000m * scale), 0, assumptions.StartDate, null, 0, "Unknown", "Personal relationship", false, "Conversation / note"),
            new("Credit Card", "Business Card", 0, 0, 0, null, null, 0, "Unknown", "", false, "Statement"),
            new("Sales Tax", "Tax Liability", 0, 0, 0, null, null, 0, "Unknown", "", false, "Tax notice")
        };

        var ownerBenefits = new List<OwnerBenefitItem>
        {
            new(DateOnly.FromDateTime(new DateTime(2026, 1, 7)), "Brian", "Landlord", "Personal rent", Round(2_500m * scale), "Personal", "Owner Draw", true, Round(2_500m * scale), "Pending accountant review", "Bank stmt"),
            new(DateOnly.FromDateTime(new DateTime(2026, 1, 10)), "Brian", "Gas Station", "Fuel", Round(300m * scale), "Mixed", "Split Review", true, Round(300m * scale), "Pending split", "Bank stmt"),
            new(DateOnly.FromDateTime(new DateTime(2026, 1, 12)), "Steven", "Restaurant", "Dinner", Round(180m * scale), "Unknown", "Review", true, Round(180m * scale), "Pending review", "Bank stmt")
        };

        var jobs = new List<JobProfitabilityItem>
        {
            new("JOB-001", "Sample Deck Job", assumptions.StartDate, DateOnly.FromDateTime(new DateTime(2026, 1, 10)), Round(12_000m * scale), Round(12_000m * scale), Round(4_500m * scale), Round(2_500m * scale), 0, Round(200m * scale), Round(300m * scale), Round(100m * scale), "Imported from reconstruction workbook pattern")
        };

        var forecast = BuildForecast(assumptions, beginningCash);

        var checks = new List<ReadinessCheck>
        {
            new("Accounting", "Bank reconciliation complete", "Manual", "100% reconciled", "Not Started", 0, "Company", DateOnly.FromDateTime(new DateTime(2026, 7, 31))),
            new("Debt", "All debt documented", "Manual", "100% documented", "Not Started", 0, "Company", DateOnly.FromDateTime(new DateTime(2026, 8, 15))),
            new("Owner Compensation", "Owner payroll implemented", "Manual", "Yes", "Not Started", 0, "Brian/Steven", DateOnly.FromDateTime(new DateTime(2026, 7, 31))),
            new("Forecasting", "13-week forecast active", "Manual", "Weekly update", "Not Started", 0, "Company", DateOnly.FromDateTime(new DateTime(2026, 7, 31))),
            new("Governance", "Written distribution policy adopted", "Manual", "Adopted", "Not Started", 0, "Company", DateOnly.FromDateTime(new DateTime(2026, 8, 31))),
            new("Vendor Trust", "Tier 1 vendor plans signed", "Manual", "100% Tier 1", "Not Started", 0, "Company", DateOnly.FromDateTime(new DateTime(2026, 8, 31))),
            new("Job Economics", "Top 20 jobs reconstructed", "Manual", "Complete", "Not Started", 0, "Company", DateOnly.FromDateTime(new DateTime(2026, 9, 15))),
            new("Expansion", $"{entityName} can operate 90 days without emergency cash", "Manual", "Yes", "Not Started", 0, "Company", DateOnly.FromDateTime(new DateTime(2026, 9, 30)))
        };

        return new FinanceEntityData(entityName, entitySlug, assumptions, bankTransactions, revenueCollections, ap, debt, ownerBenefits, jobs, forecast, checks);
    }

    private static IReadOnlyList<CashForecastWeek> BuildForecast(FinanceAssumptions assumptions, decimal beginningCash)
    {
        var weeks = new List<CashForecastWeek>();
        var currentBeginningCash = beginningCash;
        var firstWeekEnding = DateOnly.FromDateTime(new DateTime(2026, 7, 10));

        for (var week = 1; week <= 13; week++)
        {
            var expectedCollections = week <= 4 ? Round(assumptions.KnownRevenue / 26m) : Round(assumptions.KnownRevenue / 32m);
            var materials = Round(expectedCollections * 0.28m);
            var payroll = Round(expectedCollections * 0.16m);
            var subcontractors = Round(expectedCollections * 0.06m);
            var vendorPayments = week <= 8 ? 900m : 500m;
            var debtPayments = week % 4 == 0 ? 1_000m : 0m;
            var taxes = Round(expectedCollections * assumptions.SalesTaxRate);
            var rentUtilities = week % 4 == 1 ? 1_800m : 250m;
            var fuelVehicle = 450m;
            var otherOpex = 650m;
            var ownerPayroll = assumptions.WeeklyOwnerPayrollTarget;

            var forecast = new CashForecastWeek(
                week,
                firstWeekEnding.AddDays((week - 1) * 7),
                currentBeginningCash,
                expectedCollections,
                materials,
                payroll,
                subcontractors,
                vendorPayments,
                debtPayments,
                taxes,
                rentUtilities,
                fuelVehicle,
                otherOpex,
                ownerPayroll,
                assumptions.MinimumOperatingReserveTarget);

            weeks.Add(forecast);
            currentBeginningCash = forecast.EndingCash;
        }

        return weeks;
    }

    private static decimal Percent(decimal numerator, decimal denominator) => denominator == 0 ? 0 : numerator / denominator;

    private static decimal Round(decimal value) => Math.Round(value, 2);
}

