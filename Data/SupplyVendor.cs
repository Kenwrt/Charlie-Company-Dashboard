using System.ComponentModel.DataAnnotations;

namespace CharleyCompany.Dashboard.Web.Data;

public sealed class SupplyVendor
{
    public int Id { get; set; }
    [Required, StringLength(160)] public string Name { get; set; } = string.Empty;
    [StringLength(160)] public string? LegalName { get; set; }
    [StringLength(80)] public string? AccountNumber { get; set; }
    [EmailAddress, StringLength(256)] public string? Email { get; set; }
    [Phone, StringLength(40)] public string? Phone { get; set; }
    [StringLength(160)] public string? AddressLine1 { get; set; }
    [StringLength(160)] public string? AddressLine2 { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [StringLength(40)] public string? StateOrProvince { get; set; }
    [StringLength(20)] public string? PostalCode { get; set; }
    [StringLength(80)] public string Country { get; set; } = "United States";
    [StringLength(40)] public string? TaxIdentificationNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public ICollection<PayableInvoice> PayableInvoices { get; set; } = [];
    public ICollection<VendorProduct> VendorProducts { get; set; } = [];
    public ICollection<PriceImportDocument> PriceImportDocuments { get; set; } = [];
    public ICollection<CatalogSyncJob> CatalogSyncJobs { get; set; } = [];
}
