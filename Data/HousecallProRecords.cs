using System.ComponentModel.DataAnnotations;

namespace CharleyCompany.Dashboard.Web.Data;

public sealed class HousecallProJob
{
    public int Id { get; set; }
    public int LocalOperationId { get; set; }
    public LocalOperation LocalOperation { get; set; } = null!;
    [Required, StringLength(160)] public string ExternalId { get; set; } = string.Empty;
    [StringLength(100)] public string? JobNumber { get; set; }
    [StringLength(200)] public string? CustomerName { get; set; }
    [StringLength(200)] public string? CreatedByName { get; set; }
    [StringLength(256)] public string? CustomerEmail { get; set; }
    [StringLength(40)] public string? CustomerPhone { get; set; }
    [StringLength(80)] public string? WorkStatus { get; set; }
    [StringLength(80)] public string? InternalStatus { get; set; }
    [StringLength(450)] public string? InternalStatusNote { get; set; }
    [StringLength(450)] public string? InternalStatusUpdatedBy { get; set; }
    public DateTimeOffset? InternalStatusUpdatedAt { get; set; }
    [StringLength(400)] public string? Address { get; set; }
    public DateTimeOffset? ScheduledStart { get; set; }
    public DateTimeOffset? ScheduledEnd { get; set; }
    public decimal JobPrice { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal OutstandingBalance { get; set; }
    public DateTimeOffset? SourceUpdatedAt { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<HousecallProJobFollowUp> FollowUps { get; set; } = [];
    public HousecallProJobProgress? Progress { get; set; }
    public ICollection<HousecallProJobBlocker> Blockers { get; set; } = [];
    public ICollection<HousecallProJobPaymentMilestone> PaymentMilestones { get; set; } = [];
    public ICollection<HousecallProJobProgressEvent> ProgressEvents { get; set; } = [];
}

public static class JobProgressOptions
{
    public static readonly string[] Phases = ["Design", "Permitting", "HOA Approval", "Procurement", "Scheduled", "Construction", "Inspection", "Final Payment", "Complete"];
    public static readonly string[] BlockerTypes = ["Permit", "HOA", "Weather", "Customer", "Materials", "Inspection", "Crew Scheduling", "Other"];
    public static readonly string[] MilestoneStatuses = ["Pending", "Earned", "Invoiced", "Paid", "Waived"];
    public static (int Warning, int Critical) Threshold(string blockerType) => blockerType switch
    {
        "HOA" => (14, 30), "Permit" => (10, 21), "Weather" => (3, 7), "Materials" => (5, 14),
        "Inspection" => (3, 7), "Customer" => (5, 10), "Crew Scheduling" => (5, 10), _ => (7, 14)
    };
}

public sealed class HousecallProJobProgress
{
    public int Id { get; set; }
    public int HousecallProJobId { get; set; }
    public HousecallProJob HousecallProJob { get; set; } = null!;
    [Required, StringLength(80)] public string CurrentPhase { get; set; } = "Design";
    public DateTimeOffset PhaseEnteredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateOnly? ExpectedPhaseCompletionDate { get; set; }
    public DateOnly? RevisedJobCompletionDate { get; set; }
    [StringLength(500)] public string? NextAction { get; set; }
    public DateOnly? NextFollowUpDate { get; set; }
    [StringLength(200)] public string? ResponsibleParty { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [StringLength(450)] public string? UpdatedBy { get; set; }
}

public sealed class HousecallProJobBlocker
{
    public int Id { get; set; }
    public int HousecallProJobId { get; set; }
    public HousecallProJob HousecallProJob { get; set; } = null!;
    [Required, StringLength(80)] public string BlockerType { get; set; } = "Other";
    [Required, StringLength(500)] public string Description { get; set; } = string.Empty;
    public DateOnly StartedOn { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly? ExpectedResolutionDate { get; set; }
    public DateOnly? ResolvedOn { get; set; }
    [StringLength(500)] public string? NextAction { get; set; }
    public DateOnly? NextFollowUpDate { get; set; }
    [StringLength(200)] public string? ResponsibleParty { get; set; }
    public decimal RevenueAtRisk { get; set; }
    [StringLength(2000)] public string? ResolutionNotes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [StringLength(450)] public string? CreatedBy { get; set; }
}

public sealed class HousecallProJobPaymentMilestone
{
    public int Id { get; set; }
    public int HousecallProJobId { get; set; }
    public HousecallProJob HousecallProJob { get; set; } = null!;
    [Required, StringLength(160)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(80)] public string TriggerPhase { get; set; } = "Construction";
    public decimal Amount { get; set; }
    public DateOnly? ExpectedPaymentDate { get; set; }
    [Required, StringLength(40)] public string Status { get; set; } = "Pending";
    public DateOnly? PaidOn { get; set; }
    [StringLength(1000)] public string? Notes { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class HousecallProJobProgressEvent
{
    public int Id { get; set; }
    public int HousecallProJobId { get; set; }
    public HousecallProJob HousecallProJob { get; set; } = null!;
    [Required, StringLength(80)] public string EventType { get; set; } = string.Empty;
    [Required, StringLength(500)] public string Summary { get; set; } = string.Empty;
    [StringLength(2000)] public string? Details { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    [StringLength(450)] public string? EnteredBy { get; set; }
}

public sealed class HousecallProEstimate
{
    public int Id { get; set; }
    public int LocalOperationId { get; set; }
    public LocalOperation LocalOperation { get; set; } = null!;
    [Required, StringLength(160)] public string ExternalId { get; set; } = string.Empty;
    [StringLength(100)] public string? EstimateNumber { get; set; }
    [StringLength(200)] public string? CustomerName { get; set; }
    [StringLength(200)] public string? CreatedByName { get; set; }
    [StringLength(256)] public string? CustomerEmail { get; set; }
    [StringLength(40)] public string? CustomerPhone { get; set; }
    [StringLength(400)] public string? CustomerAddress { get; set; }
    [StringLength(80)] public string? Status { get; set; }
    [StringLength(80)] public string? ApprovalStatus { get; set; }
    [StringLength(80)] public string? InternalStatus { get; set; }
    [StringLength(450)] public string? InternalStatusNote { get; set; }
    [StringLength(450)] public string? InternalStatusUpdatedBy { get; set; }
    public DateTimeOffset? InternalStatusUpdatedAt { get; set; }
    public DateTimeOffset? EstimateDate { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTimeOffset? SourceUpdatedAt { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<HousecallProEstimateCommunication> Communications { get; set; } = [];
    public ICollection<HousecallProEstimateFollowUp> FollowUps { get; set; } = [];
}

public static class HousecallProEstimateStatuses
{
    public const string New = "New";
    public const string FollowUp = "Follow Up";
    public const string FollowUpPending = "Follow Up Pending";
    public const string FollowUpComplete = "Follow Up Complete";
    public static readonly string[] All = [New, FollowUp, FollowUpPending, FollowUpComplete];
}

public sealed class HousecallProEstimateCommunication
{
    public int Id { get; set; }
    public int HousecallProEstimateId { get; set; }
    public HousecallProEstimate HousecallProEstimate { get; set; } = null!;
    [Required, StringLength(40)] public string CommunicationType { get; set; } = "Phone";
    [Required, StringLength(40)] public string Direction { get; set; } = "Outbound";
    [Required, StringLength(4000)] public string Notes { get; set; } = string.Empty;
    [StringLength(450)] public string? EnteredByUserId { get; set; }
    [Required, StringLength(450)] public string EnteredByName { get; set; } = string.Empty;
    public DateTimeOffset EnteredAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class HousecallProEstimateFollowUp
{
    public int Id { get; set; }
    public int HousecallProEstimateId { get; set; }
    public HousecallProEstimate HousecallProEstimate { get; set; } = null!;
    [Required, StringLength(80)] public string Status { get; set; } = HousecallProEstimateStatuses.FollowUp;
    [Required, StringLength(2000)] public string Notes { get; set; } = string.Empty;
    [StringLength(450)] public string? EnteredByUserId { get; set; }
    [Required, StringLength(450)] public string EnteredByName { get; set; } = string.Empty;
    public DateTimeOffset EnteredAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class HousecallProJobFollowUp
{
    public int Id { get; set; }
    public int HousecallProJobId { get; set; }
    public HousecallProJob HousecallProJob { get; set; } = null!;
    [Required, StringLength(80)] public string Status { get; set; } = HousecallProEstimateStatuses.FollowUp;
    [Required, StringLength(2000)] public string Notes { get; set; } = string.Empty;
    [StringLength(450)] public string? EnteredByUserId { get; set; }
    [Required, StringLength(450)] public string EnteredByName { get; set; } = string.Empty;
    public DateTimeOffset EnteredAt { get; set; } = DateTimeOffset.UtcNow;
}
