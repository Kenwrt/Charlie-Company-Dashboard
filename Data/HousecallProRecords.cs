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
    [StringLength(400)] public string? Address { get; set; }
    public DateTimeOffset? ScheduledStart { get; set; }
    public DateTimeOffset? ScheduledEnd { get; set; }
    public decimal JobPrice { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal OutstandingBalance { get; set; }
    public DateTimeOffset? SourceUpdatedAt { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; } = DateTimeOffset.UtcNow;
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
