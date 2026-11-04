using System.Text.Json;

namespace CharleyCompany.Dashboard.Web.Models;

public sealed class SupplierInvoicePayload
{
    public string InvoiceNumber { get; set; } = "";
    public DateOnly? InvoiceDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public int? LocalOperationId { get; set; }
    public string PurchaseEntityCode { get; set; } = "";
    public string AccountingPolicy { get; set; } = "ReportingOnly";
    public string AccountingHandoffStatus { get; set; } = "NotEligible";
    public string AccountingPolicyVersion { get; set; } = "";
    public string PayingEntityCode { get; set; } = "";
    public string YourReference { get; set; } = "";
    public string LocationAssignmentSource { get; set; } = "";
    public string LocationAssignmentReason { get; set; } = "";
    public bool LocationRequiresReview { get; set; }
    public bool LocationAssignmentConfirmed { get; set; }
    public string LocationOverrideReason { get; set; } = "";
    public bool JobUnassigned { get; set; }
    public string JobUnassignedReason { get; set; } = "";
    public string JobNumber { get; set; } = "";
    public List<string> MatchedJobNumbers { get; set; } = [];
    public string JobLookupMessage { get; set; } = "";
    public string JobLookupReference { get; set; } = "";
    public int? JobLookupLocationId { get; set; }
    public string JobLookupFilledValue { get; set; } = "";
    public DateTimeOffset? JobLookupAt { get; set; }
    public DateTimeOffset? JobLookupSourceSyncedAt { get; set; }
    public string Currency { get; set; } = "USD";
    public bool IsCreditNote { get; set; }
    public decimal Tax { get; set; }
    public decimal Freight { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public decimal AmountPaid { get; set; }
    public bool PaymentStateVerified { get; set; }
    public bool ExtractionComplete { get; set; }
    public string ExtractionVersion { get; set; } = "";
    public Dictionary<string, string> ExtractionEvidence { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<SupplierInvoiceItem> Lines { get; set; } = [];

    public void ApplyDefaultDueDate()
    {
        if (DueDate is null && InvoiceDate is DateOnly date && date <= DateOnly.MaxValue.AddDays(-30))
            DueDate = date.AddDays(30);
    }

    public bool HasSupportedPostingPrecision() =>
        new[] { Total, Tax, Freight, Discount, AmountPaid }.All(x => decimal.Round(x, 2) == x)
        && Lines.All(x => decimal.Round(x.Quantity, 4) == x.Quantity && decimal.Round(x.UnitPrice, 4) == x.UnitPrice
            && decimal.Round(x.PackageQuantity, 4) == x.PackageQuantity && decimal.Round(x.LineTotal, 2) == x.LineTotal);

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

public sealed class SupplierInvoiceItem
{
    public string Sku { get; set; } = "";
    public string Description { get; set; } = "";
    public string PurchaseUnit { get; set; } = "Each";
    public decimal PackageQuantity { get; set; } = 1;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}
