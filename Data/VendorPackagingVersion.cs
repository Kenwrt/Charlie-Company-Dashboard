using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace CharleyCompany.Dashboard.Web.Data;
public sealed class VendorPackagingVersion
{
    public int Id { get; set; }
    public int VendorProductId { get; set; }
    public VendorProduct VendorProduct { get; set; } = null!;
    public DateOnly EffectiveDate { get; set; }
    [StringLength(40)] public string PurchaseUnit { get; set; } = "";
    [Column(TypeName = "numeric(18,4)")] public decimal? PackageQuantity { get; set; }
    public bool IsConfirmed { get; set; }
    [StringLength(500)] public string Evidence { get; set; } = "";
    [StringLength(450)] public string? ConfirmedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
