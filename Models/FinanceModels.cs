namespace CharleyCompany.Dashboard.Web.Models;

public sealed record FinanceAssumptions(
    DateOnly StartDate,
    DateOnly EndDate,
    decimal KnownRevenue,
    decimal KnownAccountingProfit,
    decimal MinimumOperatingReserveTarget,
    int ApPolicyLimitDays,
    decimal WeeklyOwnerPayrollTarget,
    decimal SalesTaxRate,
    DateOnly ReviewDate);

public sealed record BankTransaction(
    DateOnly Date,
    string Account,
    string Description,
    string TransactionType,
    decimal Amount,
    string BookCategory,
    string BusinessClassification,
    string Owner,
    bool MatchedToRevenue,
    bool MatchedToApOrDebt,
    string SourceRef);

public sealed record RevenueCollection(
    DateOnly Date,
    string CustomerOrJob,
    string InvoiceNumber,
    string HousecallProRef,
    decimal InvoiceAmount,
    decimal CollectedAmount,
    string PaymentMethod,
    bool DepositedToBank,
    string SourceRef);

public sealed record AccountsPayableItem(
    string Vendor,
    string Tier,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    decimal OriginalAmount,
    decimal PaymentsMade,
    string PaymentPlanStatus,
    decimal ProposedWeeklyPayment,
    string CriticalNotes,
    string SourceRef)
{
    public decimal OpenBalance => OriginalAmount - PaymentsMade;

    public int DaysPastDue(DateOnly asOfDate) => Math.Max(0, asOfDate.DayNumber - DueDate.DayNumber);

    public string AgingBucket(DateOnly asOfDate)
    {
        var daysPastDue = DaysPastDue(asOfDate);

        return daysPastDue switch
        {
            0 => "Current",
            <= 30 => "1-30",
            <= 60 => "31-60",
            <= 90 => "61-90",
            _ => "90+"
        };
    }
}

public sealed record DebtItem(
    string Creditor,
    string DebtType,
    decimal OriginalAmount,
    decimal CurrentBalance,
    decimal InterestRate,
    DateOnly? StartDate,
    DateOnly? MaturityDate,
    decimal MonthlyPayment,
    string PastDueStatus,
    string CollateralOrGuarantee,
    bool RecordedInBooks,
    string SourceRef);

public sealed record OwnerBenefitItem(
    DateOnly Date,
    string Owner,
    string Payee,
    string Description,
    decimal Amount,
    string BusinessClassification,
    string RecommendedClassification,
    bool TaxReviewNeeded,
    decimal ReclassAmount,
    string ApprovedTreatment,
    string SourceRef);

public sealed record JobProfitabilityItem(
    string JobNumber,
    string Customer,
    DateOnly? StartDate,
    DateOnly? EndDate,
    decimal ContractAmount,
    decimal CollectedAmount,
    decimal Materials,
    decimal Labor,
    decimal Subcontractors,
    decimal PermitsAndFees,
    decimal EquipmentRental,
    decimal OtherDirectCost,
    string Notes)
{
    public decimal GrossProfit => ContractAmount - Materials - Labor - Subcontractors - PermitsAndFees - EquipmentRental - OtherDirectCost;

    public decimal GrossMargin => ContractAmount == 0 ? 0 : GrossProfit / ContractAmount;
}

public sealed record CashForecastWeek(
    int WeekNumber,
    DateOnly WeekEnding,
    decimal BeginningCash,
    decimal ExpectedCollections,
    decimal Materials,
    decimal Payroll,
    decimal Subcontractors,
    decimal VendorPaymentPlans,
    decimal DebtPayments,
    decimal Taxes,
    decimal RentUtilities,
    decimal FuelVehicle,
    decimal OtherOperatingExpenses,
    decimal OwnerPayroll,
    decimal MinimumReserveTarget)
{
    public decimal TotalCashUses => Materials + Payroll + Subcontractors + VendorPaymentPlans + DebtPayments + Taxes + RentUtilities + FuelVehicle + OtherOperatingExpenses + OwnerPayroll;

    public decimal NetWeeklyCashFlow => ExpectedCollections - TotalCashUses;

    public decimal EndingCash => BeginningCash + NetWeeklyCashFlow;

    public decimal ReserveSurplusOrDeficit => EndingCash - MinimumReserveTarget;
}

public sealed record ReadinessCheck(
    string Category,
    string Test,
    string CurrentResult,
    string Threshold,
    string Status,
    decimal Score,
    string Owner,
    DateOnly TargetDate);

public sealed record FinanceEntityData(
    string EntityName,
    string EntitySlug,
    FinanceAssumptions Assumptions,
    IReadOnlyList<BankTransaction> BankTransactions,
    IReadOnlyList<RevenueCollection> RevenueCollections,
    IReadOnlyList<AccountsPayableItem> AccountsPayable,
    IReadOnlyList<DebtItem> DebtItems,
    IReadOnlyList<OwnerBenefitItem> OwnerBenefits,
    IReadOnlyList<JobProfitabilityItem> JobProfitability,
    IReadOnlyList<CashForecastWeek> CashForecast,
    IReadOnlyList<ReadinessCheck> ManualReadinessChecks);

public sealed record FinanceSummary(
    string EntityName,
    string EntitySlug,
    decimal KnownRevenue,
    decimal KnownAccountingProfit,
    decimal OpenAccountsPayable,
    decimal DebtOutstanding,
    decimal OwnerBenefits,
    decimal ActualEndingCash,
    decimal CalculatedEndingCash,
    decimal CashVariance,
    decimal ReserveSurplusOrDeficit,
    decimal AdjustedEconomicProfitProxy,
    decimal OpenApAsPercentOfRevenue,
    decimal DebtAsPercentOfRevenue,
    decimal OwnerBenefitsAsPercentOfRevenue,
    int MaxApDaysPastDue,
    decimal ReadinessScore);

public sealed record FinanceDashboard(
    FinanceSummary Summary,
    FinanceEntityData EntityData,
    IReadOnlyList<ReadinessCheck> ReadinessChecks);

public sealed record VentureFinanceDashboard(
    FinanceSummary Rollup,
    IReadOnlyList<FinanceDashboard> LocalDashboards);

