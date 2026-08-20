using System.ComponentModel.DataAnnotations;

namespace CharleyCompany.Dashboard.Web.Data;

public sealed class VendorCredit
{
    public int Id { get; set; }
    public int SupplyVendorId { get; set; }
    public SupplyVendor SupplyVendor { get; set; } = null!;
    public int LocalOperationId { get; set; }
    public LocalOperation LocalOperation { get; set; } = null!;
    [Required, StringLength(100)] public string Reference { get; set; } = string.Empty;
    public DateOnly CreditDate { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal AvailableAmount { get; set; }
    [StringLength(1000)] public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
