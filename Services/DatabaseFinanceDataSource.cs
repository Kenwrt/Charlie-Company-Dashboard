using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class DatabaseFinanceDataSource(IDbContextFactory<ApplicationDbContext> dbContextFactory) : IFinanceDataSource
{
    public async Task<VentureFinanceDashboard> GetVentureFinanceDashboardAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var slugs = await db.LocalOperations.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => x.Slug)
            .ToListAsync(cancellationToken);

        var dashboards = new List<FinanceDashboard>(slugs.Count);
        foreach (var slug in slugs)
        {
            var dashboard = await BuildEntityAsync(db, slug, cancellationToken);
            if (dashboard is not null) dashboards.Add(dashboard);
        }

        var rollup = BuildRollup(dashboards);
        return new VentureFinanceDashboard(rollup, dashboards, BuildRollupAudits(dashboards));
    }

    public async Task<FinanceDashboard?> GetEntityFinanceDashboardAsync(string entitySlug, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await BuildEntityAsync(db, entitySlug, cancellationToken);
    }

    private static async Task<FinanceDashboard?> BuildEntityAsync(ApplicationDbContext db, string slug, CancellationToken cancellationToken)
    {
        var operation = await db.LocalOperations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IsActive && x.Slug == slug, cancellationToken);
        if (operation is null) return null;

        var profile = await db.FinanceProfiles.AsNoTracking()
            .Include(x => x.Debts)
            .Include(x => x.OwnerAdjustments)
            .Include(x => x.ScheduledCashUses)
            .Include(x => x.ReadinessControls)
            .SingleOrDefaultAsync(x => x.LocalOperationId == operation.Id, cancellationToken)
            ?? DefaultProfile(operation.Id);

        var periodStart = profile.ReportingPeriodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var periodEndExclusive = profile.ReportingPeriodEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var jobs = await db.HousecallProJobs.AsNoTracking()
            .Where(x => x.LocalOperationId == operation.Id)
            .Where(x => x.ScheduledStart == null || (x.ScheduledStart >= periodStart && x.ScheduledStart < periodEndExclusive))
            .ToListAsync(cancellationToken);

        var payableRows = await db.PayableInvoices.AsNoTracking()
            .Include(x => x.SupplyVendor)
            .Where(x => x.LocalOperationId == operation.Id && x.Status != "Paid" && x.Status != "Void")
            .OrderBy(x => x.DueDate)
            .ToListAsync(cancellationToken);

        var milestones = await db.HousecallProJobPaymentMilestones.AsNoTracking()
            .Include(x => x.HousecallProJob)
            .Where(x => x.HousecallProJob.LocalOperationId == operation.Id)
            .Where(x => x.Status != "Paid" && x.Status != "Waived" && x.ExpectedPaymentDate != null)
            .ToListAsync(cancellationToken);

        var revenueCollections = jobs.Select(job =>
        {
            var invoiced = job.TotalAmount > 0 ? job.TotalAmount : job.JobPrice;
            var collected = Math.Max(0m, invoiced - job.OutstandingBalance);
            return new RevenueCollection(
                DateOnly.FromDateTime((job.ScheduledStart ?? job.SourceUpdatedAt ?? job.LastSyncedAt).DateTime),
                job.CustomerName ?? job.JobNumber ?? job.ExternalId,
                job.JobNumber ?? job.ExternalId,
                job.ExternalId,
                invoiced,
                collected,
                "Housecall Pro",
                true,
                "CCV synchronized Housecall Pro job");
        }).ToList();
        var knownRevenue = revenueCollections.Sum(x => x.CollectedAmount);

        var ap = payableRows.Select(x => new AccountsPayableItem(
            x.SupplyVendor.Name,
            "Vendor",
            x.InvoiceNumber,
            x.InvoiceDate,
            x.DueDate,
            x.OriginalAmount,
            x.AmountPaid,
            x.Status,
            0m,
            x.Notes ?? string.Empty,
            "CCV Vendors & Payables")).ToList();

        var debts = profile.Debts.Where(x => x.IsActive).Select(x => new DebtItem(
            x.Creditor,
            x.DebtType,
            x.OriginalAmount,
            x.CurrentBalance,
            x.InterestRatePercent / 100m,
            null,
            null,
            x.MonthlyPayment,
            "Current",
            x.Notes ?? string.Empty,
            true,
            x.Source)).ToList();

        var ownerAdjustments = profile.OwnerAdjustments.Where(x => !x.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase)).Select(x => new OwnerBenefitItem(
            x.TransactionDate,
            x.Owner,
            x.Payee,
            x.Description,
            x.Amount,
            "Pending classification",
            "Accounting review",
            true,
            x.ReclassAmount,
            x.Status,
            x.Source)).ToList();

        var forecast = BuildForecast(profile, milestones, debts);
        var assumptions = new FinanceAssumptions(
            profile.ReportingPeriodStart,
            profile.ReportingPeriodEnd,
            knownRevenue,
            profile.AccountingProfit,
            profile.MinimumOperatingReserveTarget,
            profile.ApPolicyLimitDays,
            0m,
            0m,
            profile.AsOfDate);

        var calculatedControls = new List<ReadinessCheck>
        {
            new("Liquidity", "Reconciled cash meets reserve target", profile.ReconciledCashBalance.ToString("C2"), profile.MinimumOperatingReserveTarget.ToString("C2"), profile.ReconciledCashBalance >= profile.MinimumOperatingReserveTarget ? "Pass" : "Fail", profile.ReconciledCashBalance >= profile.MinimumOperatingReserveTarget ? 1m : 0m, "Finance", profile.AsOfDate),
            new("AP", $"No vendor invoices more than {profile.ApPolicyLimitDays} days past due", ap.Count == 0 ? "No open AP" : $"{ap.Max(x => x.DaysPastDue(profile.AsOfDate))} days", $"<= {profile.ApPolicyLimitDays} days", ap.Count == 0 || ap.Max(x => x.DaysPastDue(profile.AsOfDate)) <= profile.ApPolicyLimitDays ? "Pass" : "Fail", ap.Count == 0 || ap.Max(x => x.DaysPastDue(profile.AsOfDate)) <= profile.ApPolicyLimitDays ? 1m : 0m, "Finance", profile.AsOfDate)
        };
        var manualControls = profile.ReadinessControls.Select(x => new ReadinessCheck(x.Category, x.Test, x.CurrentResult, x.Threshold, x.Status, x.Status == "Pass" ? 1m : 0m, x.Owner, x.TargetDate)).ToList();
        var checks = calculatedControls.Concat(manualControls).ToList();

        var openAp = ap.Sum(x => x.OpenBalance);
        var debtBalance = debts.Sum(x => x.CurrentBalance);
        var ownerBenefits = ownerAdjustments.Sum(x => x.ReclassAmount);
        var forecastEndingCash = forecast.LastOrDefault()?.EndingCash ?? profile.ReconciledCashBalance;
        var readinessScore = checks.Count == 0 ? 0m : checks.Average(x => x.Score);
        var summary = new FinanceSummary(
            operation.EffectiveDisplayName,
            operation.Slug,
            knownRevenue,
            profile.AccountingProfit,
            openAp,
            debtBalance,
            ownerBenefits,
            profile.ReconciledCashBalance,
            forecastEndingCash,
            forecastEndingCash - profile.ReconciledCashBalance,
            profile.ReconciledCashBalance - profile.MinimumOperatingReserveTarget,
            profile.AccountingProfit - openAp - debtBalance - ownerBenefits,
            Percent(openAp, knownRevenue),
            Percent(debtBalance, knownRevenue),
            Percent(ownerBenefits, knownRevenue),
            ap.Count == 0 ? 0 : ap.Max(x => x.DaysPastDue(profile.AsOfDate)),
            readinessScore);

        var entityData = new FinanceEntityData(
            operation.EffectiveDisplayName,
            operation.Slug,
            assumptions,
            [],
            revenueCollections,
            ap,
            debts,
            ownerAdjustments,
            [],
            forecast,
            manualControls);

        return new FinanceDashboard(summary, entityData, checks, BuildAudits(profile, jobs, payableRows));
    }

    private static IReadOnlyList<CashForecastWeek> BuildForecast(FinanceProfile profile, IReadOnlyList<HousecallProJobPaymentMilestone> milestones, IReadOnlyList<DebtItem> debts)
    {
        var result = new List<CashForecastWeek>(13);
        var beginningCash = profile.ReconciledCashBalance;
        var firstEnding = profile.AsOfDate.AddDays(7);
        for (var index = 0; index < 13; index++)
        {
            var start = index == 0 ? profile.AsOfDate : firstEnding.AddDays((index - 1) * 7 + 1);
            var end = firstEnding.AddDays(index * 7);
            var collections = milestones.Where(x => x.ExpectedPaymentDate >= start && x.ExpectedPaymentDate <= end).Sum(x => x.Amount);
            var uses = profile.ScheduledCashUses.Where(x => x.IsActive && x.ExpectedDate >= start && x.ExpectedDate <= end).ToList();
            decimal Use(string category) => uses.Where(x => x.Category == category).Sum(x => x.Amount);
            var scheduledDebt = profile.Debts.Where(x => x.IsActive && x.NextPaymentDate >= start && x.NextPaymentDate <= end).Sum(x => x.MonthlyPayment);
            var week = new CashForecastWeek(
                index + 1,
                end,
                beginningCash,
                collections,
                Use("Materials"),
                Use("Payroll"),
                Use("Subcontractors"),
                Use("Vendor payment plan"),
                scheduledDebt + Use("Debt"),
                Use("Taxes"),
                Use("Rent and utilities"),
                Use("Fuel and vehicle"),
                Use("Other"),
                Use("Owner payroll"),
                profile.MinimumOperatingReserveTarget);
            result.Add(week);
            beginningCash = week.EndingCash;
        }
        return result;
    }

    private static IReadOnlyDictionary<string, FinanceMetricAudit> BuildAudits(FinanceProfile profile, IReadOnlyList<HousecallProJob> jobs, IReadOnlyList<PayableInvoice> invoices)
    {
        var revenueUpdated = jobs.Count == 0 ? profile.UpdatedAt : jobs.Max(x => x.LastSyncedAt);
        var apUpdated = invoices.Count == 0 ? profile.UpdatedAt : invoices.Max(x => x.UpdatedAt ?? x.CreatedAt);
        var manualAudit = new FinanceMetricAudit(profile.AsOfDate, "Administrator Finance Setup", profile.UpdatedAt, profile.UpdatedBy);
        return new Dictionary<string, FinanceMetricAudit>(StringComparer.OrdinalIgnoreCase)
        {
            ["KnownRevenue"] = new(profile.AsOfDate, "CCV synchronized Housecall Pro jobs: collected amount", revenueUpdated, "Housecall Pro sync"),
            ["OpenAP"] = new(profile.AsOfDate, "CCV Vendors & Payables ledger", apUpdated, "Vendor AP ledger"),
            ["Debt"] = manualAudit with { Source = "Administrator debt register" },
            ["Reserve"] = manualAudit with { Source = profile.CashSource },
            ["Readiness"] = manualAudit with { Source = "Calculated controls and administrator checklist" },
            ["AccountingProfit"] = manualAudit with { Source = profile.AccountingProfitSource },
            ["OwnerBenefits"] = manualAudit with { Source = "Administrator owner-adjustment register" },
            ["CashForecast"] = manualAudit with { Source = "HCP payment milestones and scheduled cash uses" }
        };
    }

    private static IReadOnlyDictionary<string, FinanceMetricAudit> BuildRollupAudits(IReadOnlyList<FinanceDashboard> dashboards)
    {
        var now = dashboards.SelectMany(x => x.MetricAudits.Values).Select(x => x.LastUpdatedAt).DefaultIfEmpty(DateTimeOffset.UtcNow).Max();
        return new Dictionary<string, FinanceMetricAudit>(StringComparer.OrdinalIgnoreCase)
        {
            ["KnownRevenue"] = new(DateOnly.FromDateTime(DateTime.Today), "Authorized Housecall Pro location rollup", now, "System rollup"),
            ["OpenAP"] = new(DateOnly.FromDateTime(DateTime.Today), "Authorized Vendors & Payables rollup", now, "System rollup"),
            ["Debt"] = new(DateOnly.FromDateTime(DateTime.Today), "Authorized administrator debt-register rollup", now, "System rollup"),
            ["Reserve"] = new(DateOnly.FromDateTime(DateTime.Today), "Authorized reconciled-cash rollup", now, "System rollup"),
            ["Readiness"] = new(DateOnly.FromDateTime(DateTime.Today), "Authorized readiness-control rollup", now, "System rollup")
        };
    }

    private static FinanceSummary BuildRollup(IReadOnlyList<FinanceDashboard> dashboards)
    {
        var items = dashboards.Select(x => x.Summary).ToList();
        var revenue = items.Sum(x => x.KnownRevenue);
        var openAp = items.Sum(x => x.OpenAccountsPayable);
        var debt = items.Sum(x => x.DebtOutstanding);
        var ownerBenefits = items.Sum(x => x.OwnerBenefits);
        return new FinanceSummary(
            "Charlie Company Ventures Group", "ventures", revenue, items.Sum(x => x.KnownAccountingProfit), openAp, debt, ownerBenefits,
            items.Sum(x => x.ActualEndingCash), items.Sum(x => x.CalculatedEndingCash), items.Sum(x => x.CashVariance), items.Sum(x => x.ReserveSurplusOrDeficit),
            items.Sum(x => x.AdjustedEconomicProfitProxy), Percent(openAp, revenue), Percent(debt, revenue), Percent(ownerBenefits, revenue),
            items.Count == 0 ? 0 : items.Max(x => x.MaxApDaysPastDue), items.Count == 0 ? 0 : items.Average(x => x.ReadinessScore));
    }

    private static FinanceProfile DefaultProfile(int operationId) => new()
    {
        LocalOperationId = operationId,
        ReportingPeriodStart = new DateOnly(DateTime.Today.Year, 1, 1),
        ReportingPeriodEnd = DateOnly.FromDateTime(DateTime.Today),
        AsOfDate = DateOnly.FromDateTime(DateTime.Today),
        UpdatedAt = DateTimeOffset.UtcNow,
        UpdatedBy = "Not configured"
    };

    private static decimal Percent(decimal amount, decimal total) => total == 0 ? 0 : amount / total;
}
