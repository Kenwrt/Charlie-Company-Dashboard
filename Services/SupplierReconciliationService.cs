using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Models;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;
public sealed class SupplierReconciliationService(IDbContextFactory<ApplicationDbContext> factory,
    IOptions<SupplierInvoiceOptions> options, IWebHostEnvironment environment)
{
    private static string Key(string value) => string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    public async Task<SupplierReconciliationResult> ApplyAsync(SupplierReconciliationSnapshot source, CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled || !settings.ReconciliationEnabled) throw new ArgumentException("Supplier reconciliation is disabled.");
        if (!source.Complete || source.CapturedAt > DateTimeOffset.UtcNow.AddMinutes(5) || source.CapturedAt < DateTimeOffset.UtcNow.AddHours(-24)
            || source.AccountHash.Length != 64 || source.Invoices.Count is < 1 or > 5000 || source.Credits.Count > 2000 || source.Payments.Count > 5000
            || source.Invoices.Select(x => x.SourceId).Distinct().Count() != source.Invoices.Count
            || source.Credits.Select(x => x.SourceId).Distinct().Count() != source.Credits.Count)
            throw new ArgumentException("A fresh, complete and uniquely identified supplier snapshot is required.");
        if (source.Invoices.Where(x => x.View == "outstanding").Sum(x => x.OutstandingCents) != source.Summary.OutstandingCents
            || source.Credits.Sum(x => x.AvailableCents) != source.Summary.AvailableCreditsCents
            || source.Summary.OutstandingCents - source.Summary.AvailableCreditsCents - source.Summary.AvailableDepositsCents != source.Summary.NetDueCents)
            throw new ArgumentException("Supplier source records do not reconcile to the account summary.");
        var json = JsonSerializer.Serialize(source, SupplierInvoicePayload.JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({720000000000L + settings.DecksDocksVendorId})", ct);
        var previous = await db.SupplierReconciliationRuns.AsNoTracking().SingleOrDefaultAsync(x => x.SnapshotHash == hash, ct);
        if (previous is not null) return JsonSerializer.Deserialize<SupplierReconciliationResult>(previous.ResultJson, SupplierInvoicePayload.JsonOptions)!;
        var operation = await db.LocalOperations.SingleAsync(x => x.Slug == "nashville" && x.IsActive, ct);
        var vendor = await db.SupplyVendors.SingleAsync(x => x.Id == settings.DecksDocksVendorId, ct);
        if (!vendor.Name.Contains("decks", StringComparison.OrdinalIgnoreCase) || !vendor.Name.Contains("docks", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The configured supplier is not Decks & Docks.");
        var last = await db.SupplierReconciliationRuns.AsNoTracking().Where(x => x.SupplyVendorId == vendor.Id && x.LocalOperationId == operation.Id)
            .OrderByDescending(x => x.CapturedAt).FirstOrDefaultAsync(ct);
        if (last is not null && (last.CapturedAt >= source.CapturedAt
            || JsonSerializer.Deserialize<SupplierReconciliationSnapshot>(last.SourceJson, SupplierInvoicePayload.JsonOptions)!.AccountHash != source.AccountHash))
            throw new ArgumentException("Supplier account scope changed or the snapshot is older than the last reconciliation.");
        var result = new SupplierReconciliationResult { SupplierNetDue = source.Summary.NetDueCents / 100m, SourcePaymentRecords = source.Payments.Count };
        void Flag(string kind, string reference, string detail) => result.Issues.Add(new(kind, reference, detail));
        var invoices = await db.PayableInvoices.Where(x => x.SupplyVendorId == vendor.Id && x.LocalOperationId == operation.Id).ToListAsync(ct);
        var imports = await db.SupplierInvoiceImports.Where(x => x.PayableInvoiceId != null && x.PriceImportDocument.SupplyVendorId == vendor.Id).ToListAsync(ct);
        var matched = new HashSet<int>();
        foreach (var row in source.Invoices)
        {
            var candidates = invoices.Where(x => Key(x.InvoiceNumber) == Key(row.Number)).ToList();
            if (candidates.Count != 1) { Flag("UnmatchedSupplierInvoice", row.Number, candidates.Count == 0 ? "No dashboard invoice; original collection/intake may still need review." : "Multiple dashboard invoice matches."); continue; }
            var invoice = candidates[0];
            if (source.Invoices.Count(x => Key(x.Number) == Key(row.Number)) != 1) { Flag("AmbiguousSupplierInvoice", row.Number, "More than one source record uses this invoice number."); continue; }
            if (invoice.OriginalAmount != row.AmountCents / 100m || invoice.InvoiceDate != row.IssueDate)
            { Flag("InvoiceIdentityMismatch", row.Number, "Original amount or invoice date differs. No balance changed."); continue; }
            if (row.AmountCents <= 0 || row.OutstandingCents < 0 || row.OutstandingCents > row.AmountCents
                || row.View is not ("paid" or "outstanding") || row.View == "paid" && row.OutstandingCents != 0)
            { Flag("InvalidSourceBalance", row.Number, "Supplier balance or status requires investigation."); continue; }
            if (invoice.UpdatedAt > source.CapturedAt)
            { Flag("DashboardChangedAfterSnapshot", row.Number, "Dashboard invoice was edited after collection; newer local changes retained."); continue; }
            matched.Add(invoice.Id); result.MatchedInvoices++;
            var settled = (row.AmountCents - row.OutstandingCents) / 100m;
            var newStatus = row.OutstandingCents == 0 ? "Paid" : settled > 0 ? "Partially Paid" : "Open";
            if (invoice.AmountPaid != settled || invoice.Status != newStatus || invoice.DueDate != row.DueDate)
            {
                result.UpdatedInvoices++;
                db.OperationalEvents.Add(new OperationalEvent { Environment = environment.EnvironmentName, Machine = System.Environment.MachineName,
                    Service = "CharlieCompanyDashboard", Module = "SupplierReconciliation", LocalOperationId = operation.Id,
                    CorrelationId = hash, Step = "InvoiceBalanceReconciled", Status = "Completed", Message = "Supplier-confirmed balance applied; original invoice and lines preserved.",
                    MetadataJson = JsonSerializer.Serialize(new { invoiceId = invoice.Id, row.SourceId, previousSettled = invoice.AmountPaid,
                        settled, previousDueDate = invoice.DueDate, sourceDueDate = row.DueDate, row.OutstandingCents, row.SettlementReference }) });
                invoice.AmountPaid = settled; invoice.Status = newStatus;
                if (row.DueDate >= row.IssueDate) invoice.DueDate = row.DueDate;
                else Flag("InvalidDueDate", row.Number, "Supplier due date precedes invoice date; dashboard date retained.");
                invoice.UpdatedAt = DateTimeOffset.UtcNow;
            }
            if (row.PendingCents > 0) Flag("PendingSupplierPayment", row.Number, "Pending payment exists; it was not independently applied as a settled payment.");
            var allocations = source.Payments.SelectMany(x => x.Allocations).Where(x => x.SourceInvoiceId == row.SourceId).ToList();
            if (settled > 0 && allocations.Count == 0 && row.SettlementReference is null)
                Flag("SettlementBreakdownUnavailable", row.Number, "Supplier confirms the remaining balance, but cash/credit allocation detail is not available in this account view.");
            foreach (var import in imports.Where(x => x.PayableInvoiceId == invoice.Id))
            {
                var payload = JsonNode.Parse(import.PayloadJson)!.AsObject();
                payload["amountPaid"] = settled; payload["paymentStateVerified"] = true;
                payload["supplierReconciliation"] = JsonSerializer.SerializeToNode(new { snapshotHash = hash, source.CapturedAt,
                    row.SourceId, row.OutstandingCents, settledAmount = settled, settlementMayIncludeCreditsOrAdjustments = true }, SupplierInvoicePayload.JsonOptions);
                import.PayloadJson = payload.ToJsonString(SupplierInvoicePayload.JsonOptions);
                import.ReviewedAt = DateTimeOffset.UtcNow;
            }
        }
        foreach (var invoice in invoices.Where(x => !matched.Contains(x.Id)))
            Flag("UnmatchedDashboardInvoice", invoice.InvoiceNumber, "No unique amount/date/number match in supplier records; balance retained.");
        foreach (var payment in source.Payments)
        {
            if (payment.Allocations.Count == 0) Flag("UnallocatedPayment", payment.SourceId, "Payment has no invoice allocations in the accessible records.");
            foreach (var allocation in payment.Allocations)
                if (!source.Invoices.Any(x => x.SourceId == allocation.SourceInvoiceId && Key(x.Number) == Key(allocation.InvoiceNumber)))
                    Flag("UnmatchedPaymentAllocation", allocation.InvoiceNumber, "Payment allocation does not match a collected supplier invoice.");
        }
        var credits = await db.VendorCredits.Where(x => x.SupplyVendorId == vendor.Id && x.LocalOperationId == operation.Id).ToListAsync(ct);
        var matchedCredits = new HashSet<int>();
        foreach (var row in source.Credits)
        {
            if (row.AmountCents <= 0 || row.AvailableCents < 0 || row.AvailableCents > row.AmountCents || string.IsNullOrWhiteSpace(row.Reference) || row.Reference.Length > 100)
            { Flag("InvalidSupplierCredit", row.Reference, "Credit amounts or reference need review."); continue; }
            var exact = credits.Where(x => Key(x.Reference) == Key(row.Reference)).ToList();
            if (exact.Count == 0)
            {
                // A unique amount/date pair can identify a manually entered credit with a different label.
                exact = credits.Where(x => x.OriginalAmount == row.AmountCents / 100m && x.CreditDate == row.IssueDate && !matchedCredits.Contains(x.Id)).ToList();
                if (source.Credits.Count(x => x.AmountCents == row.AmountCents && x.IssueDate == row.IssueDate) != 1) exact = [];
            }
            if (exact.Count > 1 || exact.Count == 1 && exact[0].OriginalAmount != row.AmountCents / 100m)
            { Flag("CreditMismatch", row.Reference, "Credit identity or amount is ambiguous; dashboard credit retained."); continue; }
            VendorCredit credit;
            if (exact.Count == 0)
            {
                credit = new VendorCredit { SupplyVendorId = vendor.Id, LocalOperationId = operation.Id, Reference = row.Reference,
                    CreditDate = row.IssueDate, OriginalAmount = row.AmountCents / 100m, AvailableAmount = row.AvailableCents / 100m,
                    Notes = "Supplier credit record " + row.SourceId + ". Reconciliation " + hash };
                db.VendorCredits.Add(credit); credits.Add(credit); result.CreatedCredits++;
            }
            else
            {
                credit = exact[0];
                if (credit.UpdatedAt > source.CapturedAt)
                { Flag("DashboardCreditChangedAfterSnapshot", row.Reference, "Credit was edited after collection; local value retained."); matchedCredits.Add(credit.Id); continue; }
                if (credit.AvailableAmount != row.AvailableCents / 100m)
                {
                    db.OperationalEvents.Add(new OperationalEvent { Environment = environment.EnvironmentName,
                        Machine = System.Environment.MachineName, Service = "CharlieCompanyDashboard", Module = "SupplierReconciliation",
                        LocalOperationId = operation.Id, CorrelationId = hash, Step = "CreditBalanceReconciled", Status = "Completed",
                        Message = "Supplier credit availability updated from matched credit record.",
                        MetadataJson = JsonSerializer.Serialize(new { creditId = credit.Id, previousAvailable = credit.AvailableAmount, available = row.AvailableCents / 100m, row.SourceId }) });
                    credit.AvailableAmount = row.AvailableCents / 100m; credit.UpdatedAt = DateTimeOffset.UtcNow; result.UpdatedCredits++; }
            }
            matchedCredits.Add(credit.Id);
        }
        foreach (var credit in credits.Where(x => x.Id > 0 && !matchedCredits.Contains(x.Id)))
            Flag("UnmatchedDashboardCredit", credit.Reference, "No matching supplier credit; available balance retained for review.");
        if (source.Summary.AvailableDepositsCents != 0) Flag("SupplierDeposits", "Account", "Supplier deposits are not represented as vendor credits and require separate review.");
        result.DashboardNetDue = Math.Max(0, invoices.Sum(x => Math.Max(0, x.OriginalAmount - x.AmountPaid)) - credits.Sum(x => x.AvailableAmount));
        if (result.DashboardNetDue != result.SupplierNetDue) Flag("AccountBalanceDifference", "Account", "Dashboard and supplier net totals differ; no balancing adjustment was invented.");
        var run = new SupplierReconciliationRun { SnapshotHash = hash, SupplyVendorId = vendor.Id, LocalOperationId = operation.Id,
            CapturedAt = source.CapturedAt, SourceJson = json, ResultJson = "{}" };
        db.SupplierReconciliationRuns.Add(run); await db.SaveChangesAsync(ct);
        result.RunId = run.Id; run.ResultJson = JsonSerializer.Serialize(result, SupplierInvoicePayload.JsonOptions);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return result;
    }
}
