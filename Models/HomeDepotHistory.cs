using System.Globalization;
using System.Text.Json;

namespace CharleyCompany.Dashboard.Web.Models;

public sealed class HomeDepotHistoryState
{
    public int SchemaVersion { get; set; } = 1;
    public long Revision { get; set; }
    public string AccountScope { get; set; } = "";
    public List<HomeDepotDocument> Documents { get; set; } = [];
    public List<HomeDepotPurchase> Purchases { get; set; } = [];
    public List<HomeDepotAudit> Audit { get; set; } = [];
    public int? SupplyVendorId { get; set; }
    public List<HomeDepotJobReferenceLink> JobReferenceLinks { get; set; } = [];
    public List<HomeDepotCatalogLink> CatalogLinks { get; set; } = [];
}

public sealed class HomeDepotDocument
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "Csv";
    public string Sha256 { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Coverage { get; set; } = "Partial: export range not independently established; returns, cancellations, payment evidence and receipt totals unavailable in purchase-detail CSV.";
    public string? PurchaseId { get; set; }
    public int SourceRows { get; set; }
    public List<string> Headers { get; set; } = [];
}

public sealed class HomeDepotPurchase
{
    public string Id { get; set; } = "";
    public string IdentityKey { get; set; } = "";
    public string IdentityQuality { get; set; } = "Unresolved";
    public List<HomeDepotLine> Lines { get; set; } = [];
    public HomeDepotReview Review { get; set; } = new();
    public long Version { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ExternalAccountingStatus { get; set; } = "NotSubmitted";
    public string PurchasingPolicy => "PrepaidCreditCard";
    public bool CreatesAccountsPayable => false;
    public string CardReconciliationStatus => "Deferred";
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<HomeDepotLine> ReportingLines
    {
        get
        {
            var source = Review.ReportingCsvDocumentId.Length > 0 ? Review.ReportingCsvDocumentId : Lines.FirstOrDefault()?.Sources.FirstOrDefault()?.DocumentId;
            return Lines.Where(l => l.Sources.Any(s => s.DocumentId == source));
        }
    }
}

public sealed class HomeDepotLine
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Fingerprint { get; set; } = "";
    public int Occurrence { get; set; }
    public Dictionary<string, string> Raw { get; set; } = [];
    public List<HomeDepotSource> Sources { get; set; } = [];
    public string Value(string field) => Raw.GetValueOrDefault(field, "");
    public DateOnly? Date => DateOnly.TryParse(Value("Date"), CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var date) ? date : null;
    public string Group => HomeDepotHistoryText.DisplayGroup(Value("Job Name"), Value("Project Name"));
    public bool DefaultVisible => !string.IsNullOrWhiteSpace(Value("Job Name")) || !string.IsNullOrWhiteSpace(Value("Project Name"));
    public decimal? Amount(string field) => decimal.TryParse(Value(field), NumberStyles.Currency, CultureInfo.GetCultureInfo("en-US"), out var value) ? value : null;
}

public sealed record HomeDepotSource(string DocumentId, int Row);
public sealed record HomeDepotAllocation(string JobId, string JobNumber, decimal Amount);
public sealed record HomeDepotCandidate(string JobId, string JobNumber, string Customer, string Address, string Description, int OperationId, string Operation, string Explanation, string CustomerId = "", int Rank = 0, string MatchKind = "", string MatchedReference = "");
public sealed record HomeDepotAudit(DateTimeOffset At, string Actor, string Action, string? PurchaseId, string BeforeJson, string AfterJson, string Reason);

public sealed class HomeDepotReview
{
    public string Status { get; set; } = "NeedsReview";
    public string Payer { get; set; } = "";
    public int? OperationId { get; set; }
    public string HcpCompanyId { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string Reference { get; set; } = "";
    public string ReportingCsvDocumentId { get; set; } = "";
    public string Address { get; set; } = "";
    public string TransactionType { get; set; } = "Unknown";
    public string PaymentState { get; set; } = "Unknown";
    public string PaymentEvidenceDocumentId { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public decimal? ReceiptTotal { get; set; }
    public decimal? Tax { get; set; }
    public decimal? Shipping { get; set; }
    public decimal? Fees { get; set; }
    public decimal? Discounts { get; set; }
    public bool IdentityConfirmed { get; set; }
    public string IdentityReason { get; set; } = "";
    public List<HomeDepotAllocation> Allocations { get; set; } = [];
    public List<HomeDepotMaterialAllocation> MaterialAllocations { get; set; } = [];
    public List<string> Conflicts { get; set; } = [];
    public string LookupStatus { get; set; } = "NotRequested";
    public DateTimeOffset? LookupAt { get; set; }
    public int? LookupOperationId { get; set; }
    public string LookupReference { get; set; } = "";
    public string LookupAddress { get; set; } = "";
    public string LookupMessage { get; set; } = "";
    public string SuggestedJobId { get; set; } = "";
    public string SuggestedJobNumber { get; set; } = "";
    public List<HomeDepotCandidate> Candidates { get; set; } = [];
    public string Reviewer { get; set; } = "";
    public DateTimeOffset? ReviewedAt { get; set; }
    public string Reason { get; set; } = "";
}

public sealed record HomeDepotJobReferenceLink(string SourceReference, int OperationId, string JobId,
    string JobNumber, DateOnly FromDate, DateOnly ToDate, string Reviewer, DateTimeOffset ReviewedAt, string Reason);
public sealed record HomeDepotCatalogLink(string SourceSku, int SupplyVendorId, int VendorProductId,
    string Reviewer, DateTimeOffset ReviewedAt, string Reason);
public sealed record HomeDepotMaterialAllocation(string LineId, int OperationId, string JobId,
    string JobNumber, decimal Quantity, decimal ReportedAmount);

public static class HomeDepotHistoryText
{
    public static bool IsHomeDepotSupplier(string? name) => Normalize(name ?? "").Replace(" ", "", StringComparison.Ordinal).Contains("HOMEDEPOT", StringComparison.Ordinal);
    public static string ColumnLabel(string field) => field switch
    {
        "Date" => "Purchase date", "Job Name" => "Source PO/job reference", "SKU Number" => "Supplier SKU",
        "SKU Description" => "Item description", "Quantity" => "Purchased quantity", "Internet SKU" => "Home Depot internet SKU",
        "Net Unit Price" => "Reported unit amount", "Extended Retail (before discount)" => "Reported line amount (before discount)", _ => field
    };
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    public static string Normalize(string value) => string.Join(" ", new string(value.Normalize().Select(c => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ').ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    public static string DisplayGroup(string job, string project)
    {
        var name = Normalize(string.IsNullOrWhiteSpace(job) ? project : job);
        return name is "1236 LIGHTHOUSE PLACE" or "1236 LIGHTHOUSE PL" ? "QUINN" : name;
    }
    public static readonly HashSet<string> HiddenColumns = new(StringComparer.Ordinal)
    {
        "Store Number", "Transaction ID", "Register Number", "Purchaser", "Card/Account Nickname", "Department Name", "Class Name", "Subclass Name",
        "Unit price", "Program Discount Amount", "Other Discount Amount", "Program Discount Indicator", "Order Number", "Invoice Number", "Project Name"
    };
}
