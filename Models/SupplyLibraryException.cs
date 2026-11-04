namespace CharleyCompany.Dashboard.Web.Models;

public sealed class SupplyLibraryException
{
    public string Key { get; set; } = "";
    public string Source { get; set; } = "";
    public int? CatalogVendorId { get; set; }
    public int VendorId { get; set; }
    public string Vendor { get; set; } = "";
    public string Sku { get; set; } = "";
    public int? InvoiceId { get; set; }
    public string Reference { get; set; } = "";
    public string Location { get; set; } = "";
    public string Job { get; set; } = "";
    public DateOnly? Date { get; set; }
    public string Description { get; set; } = "";
    public string? InvoiceUnit { get; set; }
    public decimal? InvoicePackage { get; set; }
    public bool InvoicePackagingConfirmed { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? LineTotal { get; set; }
    public string? CatalogUnit { get; set; }
    public decimal? CatalogPackage { get; set; }
    public bool CatalogPackagingConfirmed { get; set; }
    public decimal? CatalogPrice { get; set; }
    public bool ComparablePrices { get; set; }
    public bool NeedsReview { get; set; } = true;
    public List<string> Categories { get; set; } = [];
    public List<string> Details { get; set; } = [];
    public string CatalogReviewUrl => "/admin/catalog-automation?exceptionSku=" + Uri.EscapeDataString(Sku) + "&exceptionVendor=" + (CatalogVendorId ?? VendorId);
    public string InvoiceUrl => $"/admin/vendors/{VendorId}/invoices/{InvoiceId}/lines";
}
