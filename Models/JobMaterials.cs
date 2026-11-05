namespace CharleyCompany.Dashboard.Web.Models;

public sealed record JobMaterialLine(string Source, string SourceRecordId, string SourceLineId,
    string DocumentId, string PurchaseId, string Supplier, int? SupplyVendorId, DateOnly? PurchaseDate,
    string SupplierSku, string Description, decimal Quantity, string PurchaseUnit,
    decimal? Amount, string Currency, string AmountBasis, string PaymentTreatment,
    string AssignmentBasis, int? VendorProductId, int? ProductId, string CatalogLinkStatus,
    bool NeedsReview, string ReviewNote);
public sealed record JobMaterialSubtotal(string Supplier, string Currency, string AmountBasis, decimal Amount, int Lines);
public sealed record HomeDepotSourceReference(string Reference, int Rows);
public sealed class JobMaterialsReport
{
    public int OperationId { get; set; }
    public string Operation { get; set; } = "";
    public string JobId { get; set; } = "";
    public string JobNumber { get; set; } = "";
    public string Customer { get; set; } = "";
    public DateTimeOffset JobLastSyncedAt { get; set; }
    public List<JobMaterialLine> Lines { get; set; } = [];
    public List<JobMaterialSubtotal> Subtotals { get; set; } = [];
    public List<string> Coverage { get; set; } = [];
    public long? HomeDepotRevision { get; set; }
    public int? HomeDepotSupplyVendorId { get; set; }
    public List<HomeDepotSourceReference> HomeDepotReferences { get; set; } = [];
    public List<HomeDepotJobReferenceLink> HomeDepotReferenceLinks { get; set; } = [];
}
