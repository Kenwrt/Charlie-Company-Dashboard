using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CharleyCompany.Dashboard.Web.Data;

public static class CostRuleCalculationMethods
{
    public const string Fixed = "Fixed";
    public const string PerProjectDay = "Per project day";
    public const string PercentOfDirectCost = "Percent of direct cost";
    public static readonly string[] All = [Fixed, PerProjectDay, PercentOfDirectCost];
}

public static class CostRuleScopes
{
    public const string Task = "Task";
    public const string Project = "Project";
    public static readonly string[] All = [Task, Project];
}

public static class ProjectWorkTypes
{
    public static readonly string[] All =
    [
        "New Construction",
        "Replacement",
        "Restoration",
        "Repair",
        "Maintenance",
        "Other"
    ];
}

public static class ExclusionRecoveryTypes
{
    public const string SupplyKit = "Standard supply kit";
    public const string CostRule = "Task or project cost rule";
    public const string CrewRate = "Included in crew rate";
    public const string GeneralOverhead = "General overhead";
    public static readonly string[] All = [SupplyKit, CostRule, CrewRate, GeneralOverhead];
}

public sealed class CostingPolicyVersion
{
    public int Id { get; set; }
    public int? LocalOperationId { get; set; }
    public LocalOperation? LocalOperation { get; set; }
    [Required, StringLength(120)] public string Name { get; set; } = "Standard Costing";
    public int RevisionNumber { get; set; } = 1;
    public DateOnly EffectiveDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    [Column(TypeName = "numeric(18,2)")] public decimal DefaultDailyCrewCost { get; set; }
    [Column(TypeName = "numeric(8,2)")] public decimal DefaultCrewSize { get; set; } = 2;
    [Column(TypeName = "numeric(8,4)")] public decimal DefaultContingencyPercent { get; set; } = 10;
    [Column(TypeName = "numeric(8,4)")] public decimal DefaultTargetMarginPercent { get; set; } = 40;
    [Column(TypeName = "numeric(18,2)")] public decimal GeneralOverheadFixed { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal GeneralOverheadPerProjectDay { get; set; }
    [Column(TypeName = "numeric(8,4)")] public decimal GeneralOverheadPercent { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal MonthlyOverheadBudget { get; set; }
    [Column(TypeName = "numeric(8,2)")] public decimal ExpectedMonthlyProductiveCrewDays { get; set; }
    [NotMapped] public decimal CalculatedOverheadPerCrewDay =>
        ExpectedMonthlyProductiveCrewDays <= 0 ? 0 : decimal.Round(MonthlyOverheadBudget / ExpectedMonthlyProductiveCrewDays, 2);
    [StringLength(1000)] public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [StringLength(450)] public string? CreatedByUserId { get; set; }
    public ICollection<CostingPolicyRule> Rules { get; set; } = [];
    public ICollection<StandardSupplyKit> SupplyKits { get; set; } = [];
    public ICollection<CrewRateCard> CrewRates { get; set; } = [];
    public ICollection<TaskMarginRule> MarginRules { get; set; } = [];
}

public sealed class StandardSupplyKit
{
    public int Id { get; set; }
    public int CostingPolicyVersionId { get; set; }
    public CostingPolicyVersion CostingPolicyVersion { get; set; } = null!;
    [Required, StringLength(120)] public string Name { get; set; } = string.Empty;
    [StringLength(100)] public string? TaskType { get; set; }
    [StringLength(60)] public string? WorkType { get; set; }
    public bool IsActive { get; set; } = true;
    [StringLength(500)] public string? Notes { get; set; }
    public ICollection<StandardSupplyKitItem> Items { get; set; } = [];
}

public sealed class StandardSupplyKitItem
{
    public int Id { get; set; }
    public int StandardSupplyKitId { get; set; }
    public StandardSupplyKit StandardSupplyKit { get; set; } = null!;
    public int VendorProductId { get; set; }
    public VendorProduct VendorProduct { get; set; } = null!;
    [Column(TypeName = "numeric(18,4)")] public decimal Quantity { get; set; } = 1;
    [Column(TypeName = "numeric(8,4)")] public decimal WastePercent { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
}

public sealed class CrewRateCard
{
    public int Id { get; set; }
    public int CostingPolicyVersionId { get; set; }
    public CostingPolicyVersion CostingPolicyVersion { get; set; } = null!;
    [Required, StringLength(100)] public string TaskType { get; set; } = string.Empty;
    [StringLength(60)] public string? WorkType { get; set; }
    [Column(TypeName = "numeric(8,2)")] public decimal CrewSize { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal DailyCrewCost { get; set; }
    public bool IsActive { get; set; } = true;
    [StringLength(500)] public string? Notes { get; set; }
}

public sealed class TaskMarginRule
{
    public int Id { get; set; }
    public int CostingPolicyVersionId { get; set; }
    public CostingPolicyVersion CostingPolicyVersion { get; set; } = null!;
    [Required, StringLength(100)] public string TaskType { get; set; } = string.Empty;
    [StringLength(60)] public string? WorkType { get; set; }
    [Column(TypeName = "numeric(8,4)")] public decimal TargetMarginPercent { get; set; }
    public bool IsActive { get; set; } = true;
    [StringLength(500)] public string? Notes { get; set; }
}

public sealed class CostingPolicyRule
{
    public int Id { get; set; }
    public int CostingPolicyVersionId { get; set; }
    public CostingPolicyVersion CostingPolicyVersion { get; set; } = null!;
    [Required, StringLength(120)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(20)] public string Scope { get; set; } = CostRuleScopes.Task;
    [StringLength(100)] public string? TaskType { get; set; }
    [Required, StringLength(40)] public string CalculationMethod { get; set; } = CostRuleCalculationMethods.Fixed;
    [Column(TypeName = "numeric(18,4)")] public decimal Rate { get; set; }
    public bool IsActive { get; set; } = true;
    [StringLength(500)] public string? Notes { get; set; }
}

public sealed class MaterialExclusionRule
{
    public int Id { get; set; }
    [Required, StringLength(200)] public string MatchPhrase { get; set; } = string.Empty;
    [StringLength(100)] public string? TaskType { get; set; }
    [Required, StringLength(500)] public string Reason { get; set; } = string.Empty;
    [StringLength(60)] public string? RecoveryType { get; set; }
    [StringLength(200)] public string? RecoveryReference { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [StringLength(450)] public string? CreatedByUserId { get; set; }
}

public sealed class QuoteTaskAnalysisExclusion
{
    public int Id { get; set; }
    public int QuoteTaskAnalysisId { get; set; }
    public QuoteTaskAnalysis QuoteTaskAnalysis { get; set; } = null!;
    public int MaterialExclusionRuleId { get; set; }
    public MaterialExclusionRule MaterialExclusionRule { get; set; } = null!;
    [Required, StringLength(500)] public string Description { get; set; } = string.Empty;
    [Required, StringLength(500)] public string Reason { get; set; } = string.Empty;
    public DateTimeOffset ExcludedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class QuoteCostSnapshot
{
    public int Id { get; set; }
    public int QuoteVersionId { get; set; }
    public QuoteVersion QuoteVersion { get; set; } = null!;
    public int CostingPolicyVersionId { get; set; }
    public CostingPolicyVersion CostingPolicyVersion { get; set; } = null!;
    public int RevisionNumber { get; set; } = 1;
    public DateTimeOffset PricedAt { get; set; } = DateTimeOffset.UtcNow;
    [StringLength(450)] public string? PricedByUserId { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal DirectCost { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal ProjectOverhead { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal Contingency { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal TotalCost { get; set; }
    [Column(TypeName = "numeric(8,4)")] public decimal TargetMarginPercent { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal SuggestedCustomerPrice { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal PriceAdjustment { get; set; }
    [StringLength(1000)] public string? AdjustmentReason { get; set; }
    [NotMapped] public decimal FinalCustomerPrice => SuggestedCustomerPrice + PriceAdjustment;
    [NotMapped] public decimal ExpectedGrossProfit => FinalCustomerPrice - TotalCost;
    [NotMapped] public decimal EffectiveMarginPercent =>
        FinalCustomerPrice <= 0 ? 0 : decimal.Round(ExpectedGrossProfit / FinalCustomerPrice * 100m, 2);
    public ICollection<QuoteTaskCostSnapshot> Tasks { get; set; } = [];
}

public sealed class QuoteTaskCostSnapshot
{
    public int Id { get; set; }
    public int QuoteCostSnapshotId { get; set; }
    public QuoteCostSnapshot QuoteCostSnapshot { get; set; } = null!;
    public int QuoteProjectTaskId { get; set; }
    public QuoteProjectTask QuoteProjectTask { get; set; } = null!;
    [Column(TypeName = "numeric(18,2)")] public decimal MaterialCost { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal RequiredSupplyCost { get; set; }
    [Column(TypeName = "numeric(8,2)")] public decimal EstimatedDays { get; set; }
    [Column(TypeName = "numeric(8,2)")] public decimal CrewSize { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal DailyCrewCost { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal LaborCost { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal TaskOverhead { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal Contingency { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal TotalCost { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal AllocatedProjectOverhead { get; set; }
    [Column(TypeName = "numeric(8,4)")] public decimal TargetMarginPercent { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal SuggestedCustomerPrice { get; set; }
    [StringLength(2000)] public string? AppliedRules { get; set; }
    public ICollection<QuoteTaskSupplyCostSnapshot> RequiredSupplies { get; set; } = [];
}

public sealed class QuoteTaskSupplyCostSnapshot
{
    public int Id { get; set; }
    public int QuoteTaskCostSnapshotId { get; set; }
    public QuoteTaskCostSnapshot QuoteTaskCostSnapshot { get; set; } = null!;
    public int? VendorProductId { get; set; }
    public VendorProduct? VendorProduct { get; set; }
    [Required, StringLength(120)] public string KitName { get; set; } = string.Empty;
    [Required, StringLength(300)] public string Description { get; set; } = string.Empty;
    [Column(TypeName = "numeric(18,4)")] public decimal Quantity { get; set; }
    [Column(TypeName = "numeric(18,4)")] public decimal UnitCost { get; set; }
    [Column(TypeName = "numeric(8,4)")] public decimal WastePercent { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal ExtendedCost { get; set; }
}
