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
    [StringLength(4000)] public string ScopeOfWork { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<QuoteProjectTaskPhoto> Photos { get; set; } = [];
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
