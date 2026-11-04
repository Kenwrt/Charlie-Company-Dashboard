namespace CharleyCompany.Dashboard.Web.Models;
public sealed class SupplierReconciliationSnapshot
{
    public DateTimeOffset CapturedAt { get; set; }
    public bool Complete { get; set; }
    public string AccountHash { get; set; } = "";
    public SupplierAccountSummary Summary { get; set; } = new();
    public List<SupplierBalanceRecord> Invoices { get; set; } = [];
    public List<SupplierCreditRecord> Credits { get; set; } = [];
    public List<SupplierPaymentRecord> Payments { get; set; } = [];
}
public sealed class SupplierAccountSummary
{
    public long OutstandingCents { get; set; }
    public long AvailableCreditsCents { get; set; }
    public long NetDueCents { get; set; }
    public long PastDueCents { get; set; }
    public long AvailableDepositsCents { get; set; }
}
public sealed class SupplierBalanceRecord
{
    public string SourceId { get; set; } = "";
    public string Number { get; set; } = "";
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string RecordType { get; set; } = "";
    public long AmountCents { get; set; }
    public long OutstandingCents { get; set; }
    public long PendingCents { get; set; }
    public string View { get; set; } = "";
    public string? SettlementReference { get; set; }
    public string? SettlementDate { get; set; }
}
public sealed class SupplierCreditRecord
{
    public string SourceId { get; set; } = "";
    public string Reference { get; set; } = "";
    public DateOnly IssueDate { get; set; }
    public long AmountCents { get; set; }
    public long AvailableCents { get; set; }
    public string Status { get; set; } = "";
}
public sealed class SupplierPaymentRecord
{
    public string SourceId { get; set; } = "";
    public string Status { get; set; } = "";
    public string DisplayStatus { get; set; } = "";
    public string EntryDate { get; set; } = "";
    public string? SettledAt { get; set; }
    public long TotalCents { get; set; }
    public long ChargedCents { get; set; }
    public long CreditsCents { get; set; }
    public long DepositsCents { get; set; }
    public List<SupplierPaymentAllocation> Allocations { get; set; } = [];
}
public sealed class SupplierPaymentAllocation
{
    public string InvoiceNumber { get; set; } = "";
    public string SourceInvoiceId { get; set; } = "";
    public long ChargeCents { get; set; }
    public long CoveredCents { get; set; }
    public long DiscountCents { get; set; }
    public long DeductionCents { get; set; }
}
public sealed record SupplierReconciliationIssue(string Kind, string Reference, string Detail);
public sealed class SupplierReconciliationResult
{
    public int RunId { get; set; }
    public int MatchedInvoices { get; set; }
    public int UpdatedInvoices { get; set; }
    public int UpdatedCredits { get; set; }
    public int CreatedCredits { get; set; }
    public int SourcePaymentRecords { get; set; }
    public decimal SupplierNetDue { get; set; }
    public decimal DashboardNetDue { get; set; }
    public List<SupplierReconciliationIssue> Issues { get; set; } = [];
    public int IssueCount => Issues.Count;
}
