using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CharleyCompany.Dashboard.Web.Data;

public sealed class VendorPrice
{
    public int Id { get; set; }
    public int VendorProductId { get; set; }
    public VendorProduct VendorProduct { get; set; } = null!;
    [Column(TypeName = "numeric(18,4)")] public decimal UnitPrice { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    [Required, StringLength(30)] public string SourceType { get; set; } = "Manual";
    [StringLength(200)] public string? SourceReference { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
