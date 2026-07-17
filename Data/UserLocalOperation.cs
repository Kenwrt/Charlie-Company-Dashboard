namespace CharleyCompany.Dashboard.Web.Data;

public sealed class UserLocalOperation
{
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
    public int LocalOperationId { get; set; }
    public LocalOperation LocalOperation { get; set; } = null!;
}
