using System.ComponentModel.DataAnnotations;

namespace CharleyCompany.Dashboard.Web.Data;

public sealed class OperationIntegration
{
    public int Id { get; set; }
    public int LocalOperationId { get; set; }
    public LocalOperation LocalOperation { get; set; } = null!;
    [Required, StringLength(80)] public string Provider { get; set; } = string.Empty;
    [Required, StringLength(300)] public string SecretReference { get; set; } = string.Empty;
    [StringLength(160)] public string? ExternalAccountId { get; set; }
    public bool IsEnabled { get; set; } = true;
}
