using System.ComponentModel.DataAnnotations;

namespace CharleyCompany.Dashboard.Web.Data;

public sealed class SupplierInvoiceImport
{
    public int Id { get; set; }
    public int PriceImportDocumentId { get; set; }
    public PriceImportDocument PriceImportDocument { get; set; } = null!;
    public int? PayableInvoiceId { get; set; }
    public PayableInvoice? PayableInvoice { get; set; }
    [Required, StringLength(40)] public string Status { get; set; } = "Needs Review";
    [Required, StringLength(100)] public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    [Required] public string PayloadJson { get; set; } = "{}";
    [Required] public string OriginalPayloadJson { get; set; } = "{}";
    [StringLength(500)] public string? ReviewReason { get; set; }
    [StringLength(450)] public string? ReviewedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAt { get; set; }
}
