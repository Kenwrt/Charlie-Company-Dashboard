using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CharleyCompany.Dashboard.Web.Data;

public sealed class PriceImportDocument
{
    public int Id { get; set; }
    public int SupplyVendorId { get; set; }
    public SupplyVendor SupplyVendor { get; set; } = null!;
    [Required, StringLength(260)] public string OriginalFileName { get; set; } = string.Empty;
    [Required, StringLength(500)] public string StoragePath { get; set; } = string.Empty;
    [Required, StringLength(64)] public string Sha256 { get; set; } = string.Empty;
    [Required, StringLength(100)] public string ContentType { get; set; } = string.Empty;
    [Required, StringLength(40)] public string Status { get; set; } = "Uploaded";
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<PriceImportRow> Rows { get; set; } = [];
}

public sealed class PriceImportRow
{
    public int Id { get; set; }
    public int PriceImportDocumentId { get; set; }
    public PriceImportDocument PriceImportDocument { get; set; } = null!;
    public int? VendorProductId { get; set; }
    public VendorProduct? VendorProduct { get; set; }
    [Required, StringLength(100)] public string VendorSku { get; set; } = string.Empty;
    [Required, StringLength(300)] public string Description { get; set; } = string.Empty;
    [Column(TypeName = "numeric(18,4)")] public decimal ProposedUnitPrice { get; set; }
    public DateOnly EffectiveDate { get; set; }
    [Column(TypeName = "numeric(5,4)")] public decimal MatchConfidence { get; set; }
    [Required, StringLength(30)] public string ReviewStatus { get; set; } = "Pending";
}

public sealed class PriceApprovalRule
{
    public int Id { get; set; }
    public int? SupplyVendorId { get; set; }
    public SupplyVendor? SupplyVendor { get; set; }
    [Column(TypeName = "numeric(8,4)")] public decimal AutoApprovePercentThreshold { get; set; } = 2;
    [Column(TypeName = "numeric(18,4)")] public decimal AutoApproveDollarThreshold { get; set; } = 1;
    public bool AutoApproveTrustedApiChanges { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class CatalogSyncJob
{
    public int Id { get; set; }
    public int SupplyVendorId { get; set; }
    public SupplyVendor SupplyVendor { get; set; } = null!;
    [Required, StringLength(80)] public string Provider { get; set; } = string.Empty;
    [Required, StringLength(30)] public string Status { get; set; } = "Queued";
    [StringLength(500)] public string? Message { get; set; }
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
