using System.ComponentModel.DataAnnotations;

namespace CharleyCompany.Dashboard.Web.Data;

public sealed class VendorProduct
{
    public int Id { get; set; }
    public int SupplyVendorId { get; set; }
    public SupplyVendor SupplyVendor { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    [Required, StringLength(100)] public string VendorSku { get; set; } = string.Empty;
    [StringLength(300)] public string? VendorDescription { get; set; }
    [Range(0.0001, double.MaxValue)] public decimal PackageQuantity { get; set; } = 1;
    [Required, StringLength(40)] public string PurchaseUnit { get; set; } = "Each";
    public bool IsActive { get; set; } = true;
    public ICollection<VendorPrice> Prices { get; set; } = [];
    public ICollection<InvoiceLine> InvoiceLines { get; set; } = [];
}
