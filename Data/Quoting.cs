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

public static class ProjectTaskTypes
{
    public static readonly string[] All =
    [
        "Deck",
        "Roofing",
        "Covered Deck / Roof Structure",
        "Screen Room",
        "Hardscape / Patio",
        "Outdoor Living Upgrade",
        "Repair / Restoration",
        "Other"
    ];
}

public sealed record ProjectPhotoRequirement(string Key, string Label, string Instructions);

public static class ProjectPhotoGuidance
{
    private static readonly ProjectPhotoRequirement Overall =
        new("overall", "Overall View", "Stand back and capture the entire project area from an angle with the building visible for reference.");

    public static IReadOnlyList<ProjectPhotoRequirement> For(string taskType) => taskType switch
    {
        "Deck" =>
        [
            Overall,
            new("length", "Length Measurement", "Show the tape at the zero point and the full deck length with readable numbers."),
            new("length-end", "Length End Point", "Capture a close, clear photograph of the far end of the length measurement."),
            new("width", "Width Measurement", "Show the tape at the zero point and the full deck width with readable numbers."),
            new("width-end", "Width End Point", "Capture a close, clear photograph of the far end of the width measurement.")
        ],
        "Roofing" =>
        [
            Overall,
            new("roof-planes", "Roof Planes", "Capture every roof plane, ridge, valley, penetration, and transition."),
            new("eaves", "Eaves and Fascia", "Show eaves, fascia, gutters, drip edge, and visible decking conditions."),
            new("pitch", "Pitch Measurement", "Capture a clear roof-pitch measurement from a safe location."),
            new("access", "Access and Staging", "Show safe ladder, material-delivery, dumpster, and staging locations.")
        ],
        "Covered Deck / Roof Structure" =>
        [
            Overall,
            new("length", "Footprint Length", "Capture the full outside-to-outside length measurement."),
            new("width", "Footprint Width", "Capture the full outside-to-outside width measurement."),
            new("roof-tie-in", "Roof Tie-In", "Show the wall, fascia, or roof area where the new structure will connect."),
            new("elevation", "Elevation and Height", "Show height measurements and the complete building elevation.")
        ],
        "Screen Room" =>
        [
            Overall,
            new("length", "Room Length", "Capture the full length measurement with both endpoints visible."),
            new("width", "Room Width", "Capture the full width measurement with both endpoints visible."),
            new("openings", "Doors and Openings", "Show every door, window, stair, and opening that affects the enclosure."),
            new("connections", "Attachment Points", "Show the floor, walls, columns, and roof connection points.")
        ],
        "Hardscape / Patio" =>
        [
            Overall,
            new("length", "Area Length", "Capture the full outside-to-outside length measurement."),
            new("width", "Area Width", "Capture the full outside-to-outside width measurement."),
            new("grade", "Grade and Drainage", "Show slopes, drainage paths, low areas, and nearby foundations."),
            new("access", "Site Access", "Show the route equipment and materials must use to reach the work area.")
        ],
        "Outdoor Living Upgrade" =>
        [
            Overall,
            new("length", "Project Length", "Capture the full project length measurement."),
            new("width", "Project Width", "Capture the full project width measurement."),
            new("utilities", "Utilities", "Show visible electrical, gas, water, and drainage connections."),
            new("access", "Site Access", "Show the material-delivery and equipment-access route.")
        ],
        "Repair / Restoration" =>
        [
            Overall,
            new("damage-close", "Damage Close-Up", "Capture sharp close-up photographs of all visible damage."),
            new("damage-context", "Damage in Context", "Show where each damaged area sits within the larger structure."),
            new("measurements", "Repair Measurements", "Show clear measurements of each damaged component."),
            new("underneath", "Underside and Access", "Show framing, underside conditions, and repair access where safely possible.")
        ],
        _ =>
        [
            Overall,
            new("measurements", "Primary Measurements", "Capture clear start points, endpoints, and readable measurements."),
            new("conditions", "Existing Conditions", "Show conditions that could affect materials, labor, or access."),
            new("access", "Site Access", "Show the route for workers, equipment, and materials.")
        ]
    };
}

public sealed class QuoteCase
{
    public int Id { get; set; }
    public int LocalOperationId { get; set; }
    public LocalOperation LocalOperation { get; set; } = null!;
    [StringLength(160)] public string? HousecallProQuoteId { get; set; }
    [StringLength(100)] public string? HousecallProEstimateNumber { get; set; }
    [StringLength(160)] public string? HousecallProJobId { get; set; }
    [StringLength(160)] public string? HousecallProCustomerId { get; set; }
    [StringLength(160)] public string? CompanyCamProjectId { get; set; }
    [StringLength(160)] public string? CustomerName { get; set; }
    [EmailAddress, StringLength(256)] public string? CustomerEmail { get; set; }
    [StringLength(500)] public string? CustomerAddress { get; set; }
    [Required, StringLength(2000)] public string WorkDescription { get; set; } = string.Empty;
    [Required, StringLength(40)] public string Status { get; set; } = QuoteStatuses.Received;
    [StringLength(450)] public string? AssignedUserId { get; set; }
    public ApplicationUser? AssignedUser { get; set; }
    [StringLength(64)] public string? LastMaterialEmailSignature { get; set; }
    public DateTimeOffset? LastMaterialEmailSentAt { get; set; }
    public DateTimeOffset? AdminCompletionEmailSentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<QuoteVersion> Versions { get; set; } = [];
    public ICollection<QuoteProjectTask> ProjectTasks { get; set; } = [];
    public ICollection<QuoteAuditEvent> AuditEvents { get; set; } = [];
    public ICollection<QuoteProcessingJob> ProcessingJobs { get; set; } = [];
}

public sealed class QuoteProjectTask
{
    public int Id { get; set; }
    public int QuoteCaseId { get; set; }
    public QuoteCase QuoteCase { get; set; } = null!;
    public int SortOrder { get; set; }
    [Required, StringLength(100)] public string TaskType { get; set; } = ProjectTaskTypes.All[0];
    [StringLength(60)] public string? WorkType { get; set; }
    [StringLength(4000)] public string ScopeOfWork { get; set; } = string.Empty;
    [Column(TypeName = "numeric(8,2)")] public decimal EstimatedDays { get; set; } = 1;
    [Column(TypeName = "numeric(8,2)")] public decimal? CrewSizeOverride { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal? DailyCrewCostOverride { get; set; }
    [Column(TypeName = "numeric(8,4)")] public decimal? ContingencyPercentOverride { get; set; }
    [Column(TypeName = "numeric(8,4)")] public decimal? TargetMarginPercentOverride { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<QuoteProjectTaskPhoto> Photos { get; set; } = [];
    public ICollection<QuoteTaskAnalysis> Analyses { get; set; } = [];
    public ICollection<QuoteTaskCostSnapshot> CostSnapshots { get; set; } = [];
}

public static class QuoteTaskAnalysisStatuses
{
    public const string NotSubmitted = "Not submitted";
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string NeedsReview = "Needs review";
    public const string Accepted = "Accepted";
    public const string Rejected = "Rejected";
    public const string Failed = "Failed";
}

public sealed class QuoteTaskAnalysis
{
    public int Id { get; set; }
    public int QuoteProjectTaskId { get; set; }
    public QuoteProjectTask QuoteProjectTask { get; set; } = null!;
    public int RevisionNumber { get; set; } = 1;
    [Required, StringLength(30)] public string Status { get; set; } = QuoteTaskAnalysisStatuses.Queued;
    [StringLength(450)] public string? SubmittedByUserId { get; set; }
    [StringLength(120)] public string? ModelVersion { get; set; }
    [StringLength(4000)] public string? Assumptions { get; set; }
    [StringLength(4000)] public string? QuestionsAndWarnings { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal DeliveryAllowance { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal TaxAllowance { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal OtherAllowance { get; set; }
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    [StringLength(450)] public string? ReviewedByUserId { get; set; }
    public ICollection<QuoteTaskAnalysisMaterial> Materials { get; set; } = [];
    public ICollection<QuoteTaskAnalysisExclusion> Exclusions { get; set; } = [];
    public ICollection<QuoteTaskAnalysisReviewItem> ReviewItems { get; set; } = [];
    [NotMapped] public decimal MaterialSubtotal => Materials.Sum(item => item.ExtendedCost);
    [NotMapped] public decimal TotalCost => MaterialSubtotal + DeliveryAllowance + TaxAllowance + OtherAllowance;
}

public sealed class QuoteTaskAnalysisMaterial
{
    public int Id { get; set; }
    public int QuoteTaskAnalysisId { get; set; }
    public QuoteTaskAnalysis QuoteTaskAnalysis { get; set; } = null!;
    public int? VendorProductId { get; set; }
    public VendorProduct? VendorProduct { get; set; }
    public int SortOrder { get; set; }
    [StringLength(100)] public string? VendorSku { get; set; }
    [Required, StringLength(500)] public string Description { get; set; } = string.Empty;
    [StringLength(500)] public string? OriginalDescription { get; set; }
    [Column(TypeName = "numeric(18,4)")] public decimal Quantity { get; set; }
    [Required, StringLength(40)] public string Unit { get; set; } = "Each";
    [Column(TypeName = "numeric(18,4)")] public decimal UnitCost { get; set; }
    [Column(TypeName = "numeric(8,4)")] public decimal WastePercent { get; set; }
    [Column(TypeName = "numeric(5,4)")] public decimal MatchConfidence { get; set; }
    [StringLength(100)] public string? SourceType { get; set; }
    [StringLength(255)] public string? SourceReference { get; set; }
    public DateOnly? SourcePriceDate { get; set; }
    public bool IsUnmatched { get; set; }
    [Required, StringLength(30)] public string MatchKind { get; set; } = MaterialMatchKinds.Unresolved;
    [Required, StringLength(30)] public string ReviewDecision { get; set; } = MaterialReviewDecisions.Pending;
    public bool IsRemoved { get; set; }
    public bool IsEstimatorLocked { get; set; }
    [StringLength(1000)] public string? Notes { get; set; }
    [NotMapped] public decimal ExtendedCost =>
        IsRemoved ? 0 : decimal.Round(Quantity * UnitCost * (1 + WastePercent / 100m), 2);
}

public sealed class QuoteTaskAnalysisReviewItem
{
    public int Id { get; set; }
    public int QuoteTaskAnalysisId { get; set; }
    public QuoteTaskAnalysis QuoteTaskAnalysis { get; set; } = null!;
    public int SortOrder { get; set; }
    [Required, StringLength(80)] public string ItemKey { get; set; } = string.Empty;
    [Required, StringLength(20)] public string ReviewKind { get; set; } = AnalysisReviewKinds.Warning;
    [Required, StringLength(100)] public string Category { get; set; } = "Review item";
    [Required, StringLength(1000)] public string Description { get; set; } = string.Empty;
    [Required, StringLength(30)] public string Status { get; set; } = AnalysisReviewStatuses.NeedsReview;
    [StringLength(2000)] public string? EstimatorResponse { get; set; }
    [StringLength(60)] public string? ResolutionAction { get; set; }
    public int? AddedVendorProductId { get; set; }
    public VendorProduct? AddedVendorProduct { get; set; }
    [Column(TypeName = "numeric(18,4)")] public decimal AddedProductQuantity { get; set; }
    [StringLength(200)] public string? AdditionalFeeName { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal AdditionalFeeAmount { get; set; }
    [StringLength(450)] public string? ResolvedByUserId { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

public static class AnalysisReviewStatuses
{
    public const string NeedsReview = "Needs review";
    public const string Accepted = "Accepted";
    public const string Resolved = "Resolved";
    public const string NotApplicable = "Not applicable";
    public const string FieldVerification = "Field verification required";
    public static readonly string[] All = [NeedsReview, Accepted, Resolved, NotApplicable, FieldVerification];
}

public static class AnalysisReviewKinds
{
    public const string Warning = "Warning";
    public const string Assumption = "Assumption";
}

public static class AssumptionResolutionActions
{
    public const string CorrectAssumption = "Correct assumption";
    public const string UpdateTaskScope = "Update task scope";
    public const string ChangeMaterials = "Change materials";
    public const string AddTaskFee = "Add task fee";
    public const string FieldVerification = "Requires field verification";
    public const string NotPartOfJob = "Not part of this job";
    public static readonly string[] All = [CorrectAssumption, UpdateTaskScope, ChangeMaterials, AddTaskFee, FieldVerification, NotPartOfJob];
}

public static class MaterialMatchKinds
{
    public const string Catalog = "Catalog";
    public const string HomeDepotExact = "Home Depot exact";
    public const string HomeDepotSimilar = "Home Depot similar";
    public const string ManualCatalog = "Manual catalog";
    public const string OneOff = "One-off";
    public const string Unresolved = "Unresolved";
}

public static class MaterialReviewDecisions
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Replaced = "Replaced";
    public const string Removed = "Removed";
}

public sealed class QuoteProjectTaskPhoto
{
    public int Id { get; set; }
    public int QuoteProjectTaskId { get; set; }
    public QuoteProjectTask QuoteProjectTask { get; set; } = null!;
    [Required, StringLength(255)] public string OriginalFileName { get; set; } = string.Empty;
    [Required, StringLength(500)] public string StoragePath { get; set; } = string.Empty;
    [Required, StringLength(100)] public string ContentType { get; set; } = "image/jpeg";
    [Required, StringLength(80)] public string RequirementKey { get; set; } = "additional";
    [Required, StringLength(160)] public string RequirementLabel { get; set; } = "Additional Photo";
    public long FileSize { get; set; }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
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
    public ICollection<QuoteCostSnapshot> CostSnapshots { get; set; } = [];
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
    public int? QuoteProjectTaskId { get; set; }
    public QuoteProjectTask? QuoteProjectTask { get; set; }
    [Required, StringLength(60)] public string JobType { get; set; } = string.Empty;
    [Required, StringLength(30)] public string Status { get; set; } = "Disabled";
    [StringLength(500)] public string? Message { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
