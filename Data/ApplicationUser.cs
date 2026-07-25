using Microsoft.AspNetCore.Identity;

namespace CharleyCompany.Dashboard.Web.Data;

// Add profile data for application users by adding properties to the ApplicationUser class
public class ApplicationUser : IdentityUser
{
    public bool MustChangePassword { get; set; }
    public ICollection<UserLocalOperation> LocalOperationMemberships { get; set; } = [];
}

