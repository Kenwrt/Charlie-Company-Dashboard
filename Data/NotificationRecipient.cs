using System.ComponentModel.DataAnnotations;

namespace CharleyCompany.Dashboard.Web.Data;

public sealed class NotificationRecipient
{
    public int Id { get; set; }

    [Required]
    [StringLength(120)]
    public string DisplayName { get; set; } = string.Empty;

    [EmailAddress]
    [StringLength(256)]
    public string? EmailAddress { get; set; }

    [Phone]
    [StringLength(32)]
    public string? CellPhoneNumber { get; set; }

    public bool EnableEmail { get; set; } = true;

    public bool EnableSms { get; set; }

    public bool EnableIMessage { get; set; }

    public bool NotifyOnQuoteEvents { get; set; } = true;

    public bool NotifyOnExpenseEvents { get; set; } = true;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}

