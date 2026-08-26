namespace CharleyCompany.Dashboard.Web.Data;

public sealed class SmsConsentEvent
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public string DisclosureVersion { get; set; } = string.Empty;
    public string DisclosureText { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}
