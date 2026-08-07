using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CharleyCompany.Dashboard.Web.Data;

public sealed class FinanceProfile
{
    public int Id { get; set; }
    public int LocalOperationId { get; set; }
    public LocalOperation LocalOperation { get; set; } = null!;
    public DateOnly ReportingPeriodStart { get; set; } = new(DateTime.Today.Year, 1, 1);
    public DateOnly ReportingPeriodEnd { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly AsOfDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [Column(TypeName = "numeric(18,2)")] public decimal ReconciledCashBalance { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal MinimumOperatingReserveTarget { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal AccountingProfit { get; set; }
    public int ApPolicyLimitDays { get; set; } = 60;
    [StringLength(160)] public string CashSource { get; set; } = "Manual bank reconciliation";
    [StringLength(160)] public string AccountingProfitSource { get; set; } = "Manual accounting entry";
    [StringLength(2000)] public string? Notes { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required, StringLength(450)] public string UpdatedBy { get; set; } = string.Empty;
    public ICollection<FinanceDebt> Debts { get; set; } = [];
    public ICollection<FinanceOwnerAdjustment> OwnerAdjustments { get; set; } = [];
    public ICollection<FinanceScheduledCashUse> ScheduledCashUses { get; set; } = [];
    public ICollection<FinanceReadinessControl> ReadinessControls { get; set; } = [];
}

public sealed class FinanceDebt
{
    public int Id { get; set; }
    public int FinanceProfileId { get; set; }
    public FinanceProfile FinanceProfile { get; set; } = null!;
    [Required, StringLength(160)] public string Creditor { get; set; } = string.Empty;
    [Required, StringLength(80)] public string DebtType { get; set; } = "Loan";
    [Column(TypeName = "numeric(18,2)")] public decimal OriginalAmount { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal CurrentBalance { get; set; }
    [Column(TypeName = "numeric(8,4)")] public decimal InterestRatePercent { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal MonthlyPayment { get; set; }
    public DateOnly? NextPaymentDate { get; set; }
    [StringLength(160)] public string Source { get; set; } = "Manual debt register";
    [StringLength(1000)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required, StringLength(450)] public string UpdatedBy { get; set; } = string.Empty;
}

public sealed class FinanceOwnerAdjustment
{
    public int Id { get; set; }
    public int FinanceProfileId { get; set; }
    public FinanceProfile FinanceProfile { get; set; } = null!;
    public DateOnly TransactionDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [Required, StringLength(160)] public string Owner { get; set; } = string.Empty;
    [Required, StringLength(160)] public string Payee { get; set; } = string.Empty;
    [Required, StringLength(500)] public string Description { get; set; } = string.Empty;
    [Column(TypeName = "numeric(18,2)")] public decimal Amount { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal ReclassAmount { get; set; }
    [Required, StringLength(80)] public string Status { get; set; } = "Pending review";
    [StringLength(160)] public string Source { get; set; } = "Manual accounting review";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required, StringLength(450)] public string UpdatedBy { get; set; } = string.Empty;
}

public sealed class FinanceScheduledCashUse
{
    public int Id { get; set; }
    public int FinanceProfileId { get; set; }
    public FinanceProfile FinanceProfile { get; set; } = null!;
    public DateOnly ExpectedDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [Required, StringLength(160)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(80)] public string Category { get; set; } = "Other";
    [Column(TypeName = "numeric(18,2)")] public decimal Amount { get; set; }
    [StringLength(160)] public string Source { get; set; } = "Manual cash schedule";
    [StringLength(1000)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required, StringLength(450)] public string UpdatedBy { get; set; } = string.Empty;
}

public sealed class FinanceReadinessControl
{
    public int Id { get; set; }
    public int FinanceProfileId { get; set; }
    public FinanceProfile FinanceProfile { get; set; } = null!;
    [Required, StringLength(100)] public string Category { get; set; } = string.Empty;
    [Required, StringLength(300)] public string Test { get; set; } = string.Empty;
    [StringLength(300)] public string CurrentResult { get; set; } = string.Empty;
    [StringLength(300)] public string Threshold { get; set; } = string.Empty;
    [Required, StringLength(40)] public string Status { get; set; } = "Not Started";
    [StringLength(160)] public string Owner { get; set; } = string.Empty;
    public DateOnly TargetDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(30));
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required, StringLength(450)] public string UpdatedBy { get; set; } = string.Empty;
}
