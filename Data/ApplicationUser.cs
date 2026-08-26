using Microsoft.AspNetCore.Identity;

namespace CharleyCompany.Dashboard.Web.Data;

// Add profile data for application users by adding properties to the ApplicationUser class
public class ApplicationUser : IdentityUser
{
    public bool MustChangePassword { get; set; }
    public bool AdminAuditEmail { get; set; }
    [PersonalData]
    public bool SmsConsentGranted { get; set; }
    [PersonalData]
    public DateTimeOffset? SmsConsentGrantedAt { get; set; }
    [PersonalData]
    public bool SmsMarketingConsent { get; set; }
    [PersonalData]
    public DateTimeOffset? SmsMarketingConsentAt { get; set; }
    [PersonalData]
    public DateTimeOffset? SmsConsentRevokedAt { get; set; }
    [PersonalData]
    public string? SmsConsentPhoneNumber { get; set; }
    [PersonalData]
    public string? SmsConsentDisclosureVersion { get; set; }
    [PersonalData]
    public bool SmsAuthorityConfirmed { get; set; }
    public ICollection<UserLocalOperation> LocalOperationMemberships { get; set; } = [];
}

