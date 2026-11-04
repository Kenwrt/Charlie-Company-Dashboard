using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CharleyCompany.Dashboard.Web.Data;

public sealed class InvoiceLine
{
    [StringLength(100)] public string? SkuSnapshot { get; set; }
    [StringLength(300)] public string? DescriptionSnapshot { get; set; }
    [StringLength(40)] public string? PurchaseUnitSnapshot { get; set; }
    [Column(TypeName = "numeric(18,4)")] public decimal? PackageQuantitySnapshot { get; set; }
    public bool PackagingConfirmed { get; set; }
    public int Id { get; set; }
    public int PayableInvoiceId { get; set; }
    public PayableInvoice PayableInvoice { get; set; } = null!;
    public int VendorProductId { get; set; }
    public VendorProduct VendorProduct { get; set; } = null!;
    [Column(TypeName = "numeric(18,4)")] public decimal Quantity { get; set; }
    [Column(TypeName = "numeric(18,4)")] public decimal UnitPrice { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal LineTotal { get; set; }
    [Required, StringLength(30)] public string PriceReviewStatus { get; set; } = "Matches Catalog";
    [StringLength(500)] public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
