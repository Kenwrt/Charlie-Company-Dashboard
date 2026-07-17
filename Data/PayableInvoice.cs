using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CharleyCompany.Dashboard.Web.Data;

public sealed class PayableInvoice
{
    public int Id { get; set; }
    public int SupplyVendorId { get; set; }
    public SupplyVendor SupplyVendor { get; set; } = null!;
    public int LocalOperationId { get; set; }
    public LocalOperation LocalOperation { get; set; } = null!;
    [Required, StringLength(100)] public string InvoiceNumber { get; set; } = string.Empty;
    [Required, StringLength(100)] public string JobNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly DueDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(30));
    [Column(TypeName = "numeric(18,2)")] public decimal OriginalAmount { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal AmountPaid { get; set; }
    [Required, StringLength(30)] public string Status { get; set; } = "Open";
    [StringLength(2000)] public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    [NotMapped] public decimal OpenBalance => OriginalAmount - AmountPaid;
    public ICollection<InvoiceLine> Lines { get; set; } = [];
}
