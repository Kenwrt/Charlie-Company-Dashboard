using System.ComponentModel.DataAnnotations;

namespace CharleyCompany.Dashboard.Web.Data;

public sealed class ScheduledReportDefinition
{
    public int Id { get; set; }
    [Required, StringLength(120)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(60)] public string ReportType { get; set; } = "daily-operations";
    [Required, StringLength(80)] public string TimeZoneId { get; set; } = "America/Chicago";
    public TimeOnly RunAtLocalTime { get; set; } = new(7, 0);
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ScheduledReportRecipient> Recipients { get; set; } = [];
    public ICollection<ScheduledReportRun> Runs { get; set; } = [];
}

public sealed class ScheduledReportRecipient
{
    public int ScheduledReportDefinitionId { get; set; }
    public ScheduledReportDefinition ScheduledReportDefinition { get; set; } = null!;
    public int NotificationRecipientId { get; set; }
    public NotificationRecipient NotificationRecipient { get; set; } = null!;
    public bool SendEmail { get; set; } = true;
    public bool SendSms { get; set; }
}

public sealed class ScheduledReportRun
{
    public long Id { get; set; }
    public int ScheduledReportDefinitionId { get; set; }
    public ScheduledReportDefinition ScheduledReportDefinition { get; set; } = null!;
    public DateOnly ScheduledLocalDate { get; set; }
    public bool IsTest { get; set; }
    [Required, StringLength(30)] public string Status { get; set; } = "Running";
    [Required, StringLength(200)] public string Title { get; set; } = string.Empty;
    [Required] public string Body { get; set; } = string.Empty;
    [StringLength(500)] public string? Error { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public ICollection<ScheduledReportAccessToken> AccessTokens { get; set; } = [];
}

public sealed class ScheduledReportAccessToken
{
    public long Id { get; set; }
    public long ScheduledReportRunId { get; set; }
    public ScheduledReportRun ScheduledReportRun { get; set; } = null!;
    public int NotificationRecipientId { get; set; }
    public NotificationRecipient NotificationRecipient { get; set; } = null!;
    [Required, StringLength(64)] public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastAccessedAt { get; set; }
}
