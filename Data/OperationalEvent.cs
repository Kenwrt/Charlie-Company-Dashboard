using System.ComponentModel.DataAnnotations;

namespace CharleyCompany.Dashboard.Web.Data;

public sealed class OperationalEvent
{
    public long Id { get; set; }
    public Guid EventId { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    [Required, StringLength(20)] public string Severity { get; set; } = "Information";
    [Required, StringLength(40)] public string Environment { get; set; } = string.Empty;
    [Required, StringLength(120)] public string Machine { get; set; } = string.Empty;
    [Required, StringLength(120)] public string Service { get; set; } = string.Empty;
    [Required, StringLength(120)] public string Module { get; set; } = string.Empty;
    public int? LocalOperationId { get; set; }
    public LocalOperation? LocalOperation { get; set; }
    [Required, StringLength(100)] public string CorrelationId { get; set; } = string.Empty;
    public int? QuoteCaseId { get; set; }
    [StringLength(160)] public string? JobNumber { get; set; }
    [Required, StringLength(120)] public string Step { get; set; } = string.Empty;
    [Required, StringLength(30)] public string Status { get; set; } = "Started";
    public long? DurationMilliseconds { get; set; }
    [Required, StringLength(2000)] public string Message { get; set; } = string.Empty;
    [StringLength(100)] public string? ErrorCode { get; set; }
    [StringLength(4000)] public string? ExceptionSummary { get; set; }
    [StringLength(4000)] public string? MetadataJson { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}
