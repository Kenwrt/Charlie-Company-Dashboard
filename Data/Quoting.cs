using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CharleyCompany.Dashboard.Web.Data;

public static class QuoteStatuses
{
    public const string Received = "Received";
    public const string AwaitingPhotos = "Awaiting Photos";
    public const string ReadyForAnalysis = "Ready for Analysis";
    public const string DraftGenerated = "Draft Generated";
    public const string OperatorReview = "Operator Review";
    public const string Approved = "Approved";
    public const string RevisionRequested = "Revision Requested";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
}

public sealed class QuoteCase
{
    public int Id { get; set; }
    public int LocalOperationId { get; set; }
    public LocalOperation LocalOperation { get; set; } = null!;
    [StringLength(160)] public string? HousecallProQuoteId { get; set; }
    [StringLength(160)] public string? HousecallProJobId { get; set; }
    [StringLength(160)] public string? HousecallProCustomerId { get; set; }
    [StringLength(160)] public string? CompanyCamProjectId { get; set; }
    [StringLength(160)] public string? CustomerName { get; set; }
    [EmailAddress, StringLength(256)] public string? CustomerEmail { get; set; }
    [Required, StringLength(2000)] public string WorkDescription { get; set; } = string.Empty;
    [Required, StringLength(40)] public string Status { get; set; } = QuoteStatuses.Received;
    [StringLength(450)] public string? AssignedUserId { get; set; }
    public ApplicationUser? AssignedUser { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<QuoteVersion> Versions { get; set; } = [];
    public ICollection<QuoteAuditEvent> AuditEvents { get; set; } = [];
    public ICollection<QuoteProcessingJob> ProcessingJobs { get; set; } = [];
}

public sealed class QuoteVersion
{
    public int Id { get; set; }
    public int QuoteCaseId { get; set; }
    public QuoteCase QuoteCase { get; set; } = null!;
    public int VersionNumber { get; set; } = 1;
    [Required, StringLength(30)] public string Status { get; set; } = "Draft";
    [Column(TypeName = "numeric(8,4)")] public decimal TaxRate { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal DiscountAmount { get; set; }
    [StringLength(2000)] public string? CustomerNotes { get; set; }
    [StringLength(450)] public string? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<QuoteLine> Lines { get; set; } = [];
    [NotMapped] public decimal Subtotal => Lines.Sum(line => line.CustomerPrice);
    [NotMapped] public decimal TaxAmount => decimal.Round(Math.Max(0, Subtotal - DiscountAmount) * TaxRate / 100m, 2);
    [NotMapped] public decimal Total => Math.Max(0, Subtotal - DiscountAmount) + TaxAmount;
}

public sealed class QuoteLine
{
    public int Id { get; set; }
    public int QuoteVersionId { get; set; }
    public QuoteVersion QuoteVersion { get; set; } = null!;
    public int SortOrder { get; set; }
    [Required, StringLength(500)] public string Description { get; set; } = string.Empty;
    [Column(TypeName = "numeric(18,4)")] public decimal Quantity { get; set; } = 1;
    [Required, StringLength(40)] public string Unit { get; set; } = "Each";
    [Column(TypeName = "numeric(18,4)")] public decimal MaterialUnitCost { get; set; }
    [Column(TypeName = "numeric(18,4)")] public decimal LaborHours { get; set; }
    [Column(TypeName = "numeric(18,4)")] public decimal LaborRate { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal EquipmentCost { get; set; }
    [Column(TypeName = "numeric(8,4)")] public decimal WastePercent { get; set; }
    [Column(TypeName = "numeric(8,4)")] public decimal MarkupPercent { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal CustomerPrice { get; set; }
    [StringLength(100)] public string Source { get; set; } = "Manual";
    [NotMapped] public decimal EstimatedCost => decimal.Round((Quantity * MaterialUnitCost * (1 + WastePercent / 100m)) + (LaborHours * LaborRate) + EquipmentCost, 2);
}

public sealed class QuotePricingRule
{
    public int Id { get; set; }
    public int LocalOperationId { get; set; }
    public LocalOperation LocalOperation { get; set; } = null!;
    [Column(TypeName = "numeric(18,4)")] public decimal DefaultLaborRate { get; set; } = 75;
    [Column(TypeName = "numeric(8,4)")] public decimal DefaultMarkupPercent { get; set; } = 30;
    [Column(TypeName = "numeric(8,4)")] public decimal DefaultWastePercent { get; set; } = 10;
    [Column(TypeName = "numeric(8,4)")] public decimal DefaultTaxRate { get; set; }
    [Column(TypeName = "numeric(8,4)")] public decimal MinimumGrossMarginPercent { get; set; } = 25;
}

public sealed class QuoteAuditEvent
{
    public int Id { get; set; }
    public int QuoteCaseId { get; set; }
    public QuoteCase QuoteCase { get; set; } = null!;
    [Required, StringLength(40)] public string PreviousStatus { get; set; } = string.Empty;
    [Required, StringLength(40)] public string NewStatus { get; set; } = string.Empty;
    [StringLength(450)] public string? UserId { get; set; }
    [StringLength(1000)] public string? Explanation { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class QuoteProcessingJob
{
    public int Id { get; set; }
    public int QuoteCaseId { get; set; }
    public QuoteCase QuoteCase { get; set; } = null!;
    [Required, StringLength(60)] public string JobType { get; set; } = string.Empty;
    [Required, StringLength(30)] public string Status { get; set; } = "Disabled";
    [StringLength(500)] public string? Message { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
