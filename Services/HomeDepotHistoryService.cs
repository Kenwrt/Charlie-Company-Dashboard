using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Models;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic.FileIO;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class HomeDepotHistoryService(HomeDepotHistoryStore store, IDbContextFactory<ApplicationDbContext> factory,
    HousecallProDataService hcp, IOptions<SupplierInvoiceOptions> accounting,
    ILogger<HomeDepotHistoryService> logger)
{
    public static string Administrator(ClaimsPrincipal user)
    {
        if (!user.IsInRole(ApplicationRoles.Administrator)) throw new UnauthorizedAccessException();
        return user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    }
    private static string Hash(string text) => HomeDepotHistoryStore.Hash(Encoding.UTF8.GetBytes(text));
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, HomeDepotHistoryText.Json);
    public Task<HomeDepotHistoryState> ReadAsync(ClaimsPrincipal user, CancellationToken ct) => store.ReadAsync(Administrator(user), ct);

    public async Task<object> ImportCsvAsync(byte[] bytes, string actor, CancellationToken ct)
    {
        if (bytes.Length is 0 or > HomeDepotHistoryStore.MaxBytes) throw new ArgumentException("Supply a CSV of at most 20 MB.");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Encoding.GetEncoding(1252).GetString(bytes); }
        // Skip Home Depot's company/export preamble, preserving it in the original bytes.
        var start = text.IndexOf("Date,Store Number,", StringComparison.Ordinal);
        if (start < 0 || start > 65536 || start > 0 && text[start - 1] is not '\n' and not '\uFEFF') throw new ArgumentException("Expected the Home Depot detailed purchase-history CSV header.");
        using var parser = new TextFieldParser(new StringReader(text[start..])) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? [];
        var required = new[] { "Date", "Store Number", "Transaction ID", "Register Number", "Project Name", "Job Name", "SKU Number", "Quantity", "Net Unit Price", "Extended Retail (before discount)", "Order Number", "Invoice Number" };
        if (headers.Length > 100 || headers.Distinct().Count() != headers.Length || required.Any(h => !headers.Contains(h))) throw new ArgumentException("The detailed CSV schema is missing required fields or contains duplicate headers.");
        var rows = new List<Dictionary<string, string>>();
        try
        {
            while (!parser.EndOfData)
            {
                var values = parser.ReadFields() ?? [];
                if (values.All(string.IsNullOrWhiteSpace)) continue;
                if (values.Length != headers.Length || values.Any(v => v.Length > 16000) || rows.Count >= 100000) throw new ArgumentException("CSV row width, field size or row limit exceeded; no rows were committed.");
                rows.Add(headers.Select((h, i) => (h, values[i])).ToDictionary(x => x.h, x => x.Item2));
            }
        }
        catch (MalformedLineException) { throw new ArgumentException("Malformed quoted CSV; no rows were committed."); }
        var hash = HomeDepotHistoryStore.Hash(bytes);
        await store.SaveOriginalAsync(hash, bytes, ct);
        var result = await store.ChangeAsync<object>(state =>
        {
            var prior = state.Documents.FirstOrDefault(x => x.Sha256 == hash && x.Kind == "Csv");
            if (prior is not null) return Task.FromResult<object>(new { documentId = prior.Id, duplicate = true, sourceRows = prior.SourceRows, coverage = prior.Coverage });
            var document = new HomeDepotDocument { Id = Guid.NewGuid().ToString("N"), Sha256 = hash, SourceRows = rows.Count, Headers = headers.ToList() };
            state.Documents.Add(document);
            var occurrences = new Dictionary<string, int>();
            var rowNumber = 0;
            foreach (var raw in rows)
            {
                rowNumber++;
                string V(string field) => raw.GetValueOrDefault(field, "").Trim();
                // Retail transaction identity is account + day + store + register + transaction.
                // Orders may contain multiple receipts; an order number alone is never sufficient.
                var strong = new[] { "Date", "Store Number", "Register Number", "Transaction ID" }.All(f => V(f).Length > 0);
                var day = DateOnly.TryParse(V("Date"), CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var parsedDay) ? parsedDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : V("Date");
                var fields = strong ? new[] { day, V("Store Number"), V("Register Number"), V("Transaction ID") }
                    : V("Invoice Number").Length > 0 && V("Date").Length > 0 ? new[] { "invoice", day, V("Store Number"), V("Invoice Number") }
                    : new[] { "unresolved", hash, rowNumber.ToString(CultureInfo.InvariantCulture) };
                var key = Hash(Serialize(new { state.AccountScope, fields }));
                var purchase = state.Purchases.SingleOrDefault(p => p.IdentityKey == key);
                if (purchase is null)
                {
                    purchase = new() { Id = Guid.NewGuid().ToString("N"), IdentityKey = key, IdentityQuality = strong ? "SourceTransaction" : fields[0] == "invoice" ? "InvoiceCandidate" : "Unresolved" };
                    state.Purchases.Add(purchase);
                }
                // Preserve repeated identical rows by occurrence, rather than a set of SKU/amounts.
                var fingerprint = Hash(Serialize(raw.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray()));
                var occurrenceKey = key + ":" + fingerprint;
                var occurrence = occurrences.GetValueOrDefault(occurrenceKey) + 1;
                occurrences[occurrenceKey] = occurrence;
                var line = purchase.Lines.SingleOrDefault(x => x.Fingerprint == fingerprint && x.Occurrence == occurrence);
                if (line is null)
                {
                    if (purchase.Lines.Any(x => x.Sources.All(s => s.DocumentId != document.Id)))
                    {
                        purchase.Review.Status = "NeedsReview";
                        purchase.Review.IdentityConfirmed = false;
                        purchase.Review.Conflicts.Add("Overlapping export adds or changes transaction lines; compare originals before counting this transaction.");
                    }
                    line = new() { Fingerprint = fingerprint, Occurrence = occurrence, Raw = raw };
                    purchase.Lines.Add(line);
                }
                line.Sources.Add(new(document.Id, rowNumber));
                purchase.Version++; purchase.ChangedAt = DateTimeOffset.UtcNow;
            }
            foreach (var purchase in state.Purchases.Where(p => p.Lines.Any(l => l.Sources.Any(s => s.DocumentId == document.Id))))
            {
                var incoming = purchase.Lines.Where(l => l.Sources.Any(s => s.DocumentId == document.Id)).Select(l => l.Id).ToHashSet();
                var reporting = purchase.ReportingLines.Select(l => l.Id).ToHashSet();
                if (!incoming.SetEquals(reporting))
                {
                    purchase.Review.Status = "NeedsReview"; purchase.Review.IdentityConfirmed = false;
                    purchase.Review.Conflicts.Add("Retained CSV snapshots disagree on transaction rows (including omissions). Select the reporting snapshot and resolve source coverage before accounting handoff.");
                }
                purchase.Review.Conflicts = purchase.Review.Conflicts.Distinct().ToList();
            }
            state.Audit.Add(new(DateTimeOffset.UtcNow, actor, "CsvReceived", null, "", Serialize(new { document.Id, hash, rows = rows.Count }), document.Coverage));
            return Task.FromResult<object>(new { documentId = document.Id, duplicate = false, sourceRows = rows.Count, coverage = document.Coverage });
        }, ct);
        logger.LogInformation("Home Depot CSV intake completed; source type {SourceType}", "DetailedCsv");
        return result;
    }

    public async Task<object> AttachPdfAsync(string purchaseId, byte[] bytes, string kind, string actor, CancellationToken ct)
    {
        if (bytes.Length < 5 || bytes.Length > HomeDepotHistoryStore.MaxBytes || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) throw new ArgumentException("Supply a PDF of at most 20 MB.");
        if (kind is not ("Receipt" or "Invoice" or "Return" or "Credit" or "Payment" or "Order")) throw new ArgumentException("Unsupported evidence type.");
        var hash = HomeDepotHistoryStore.Hash(bytes);
        await store.SaveOriginalAsync(hash, bytes, ct);
        return await store.ChangeAsync<object>(state =>
        {
            var prior = state.Documents.SingleOrDefault(x => x.Sha256 == hash && x.Kind != "Csv");
            if (prior is not null)
            {
                if (purchaseId.Length > 0 && prior.PurchaseId != purchaseId || prior.Kind != kind) throw new ArgumentException("This evidence already has a different transaction or type; review the existing record instead of duplicating it.");
                return Task.FromResult<object>(new { documentId = prior.Id, purchaseId = prior.PurchaseId, duplicate = true });
            }
            // Unlinked downloads are evidence only, never a new bill or inferred job.
            var purchase = purchaseId.Length > 0
                ? state.Purchases.SingleOrDefault(x => x.Id == purchaseId) ?? throw new ArgumentException("Selected source transaction not found.")
                : new HomeDepotPurchase { Id = Guid.NewGuid().ToString("N"), IdentityKey = "document:" + hash, IdentityQuality = "Unresolved" };
            if (purchaseId.Length == 0) state.Purchases.Add(purchase);
            purchaseId = purchase.Id;
            var document = new HomeDepotDocument { Id = Guid.NewGuid().ToString("N"), Sha256 = hash, Kind = kind, PurchaseId = purchaseId, Coverage = "Original downloaded/attached; contents and payment state require administrator review." };
            state.Documents.Add(document);
            purchase.Version++; purchase.ChangedAt = DateTimeOffset.UtcNow;
            state.Audit.Add(new(DateTimeOffset.UtcNow, actor, "EvidenceAttached", purchaseId, "", Serialize(document), "Original preserved; no inferred financial changes"));
            return Task.FromResult<object>(new { documentId = document.Id, purchaseId, duplicate = false });
        }, ct);
    }

    public async Task LinkEvidenceAsync(string documentId, string targetId, string reason, ClaimsPrincipal user, CancellationToken ct)
    {
        var actor = Administrator(user);
        if (reason.Trim().Length < 5 || reason.Length > 2000) throw new ArgumentException("Explain the evidence linkage or duplicate correction.");
        await store.ChangeAsync(state =>
        {
            var document = state.Documents.Single(d => d.Id == documentId && d.Kind != "Csv");
            var source = state.Purchases.Single(p => p.Id == document.PurchaseId);
            var target = state.Purchases.Single(p => p.Id == targetId);
            if (source.Id == target.Id) return Task.FromResult(false);
            if (source.Lines.Count > 0 || source.Review.ReviewedAt is not null || source.ExternalAccountingStatus != "NotSubmitted") throw new ArgumentException("Only unreviewed evidence-only records can be linked. Reviewed financial records require explicit conflict review.");
            var before = Serialize(document);
            document.PurchaseId = target.Id;
            if (!state.Documents.Any(d => d.PurchaseId == source.Id)) { source.Review.Status = "Excluded"; source.Review.Reason = reason; source.Review.Reviewer = actor; source.Review.ReviewedAt = DateTimeOffset.UtcNow; }
            source.Version++; source.ChangedAt = DateTimeOffset.UtcNow;
            target.Version++; target.ChangedAt = DateTimeOffset.UtcNow;
            target.Review.Status = "NeedsReview"; target.Review.IdentityConfirmed = false;
            state.Audit.Add(new(DateTimeOffset.UtcNow, actor, "EvidenceLinked", targetId, before, Serialize(document), reason));
            return Task.FromResult(true);
        }, ct);
    }

    private async Task<LocalOperation> OperationAsync(int? id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.LocalOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct)
            ?? throw new ArgumentException("Select an active purchasing operation. No default operation is inferred.");
    }

    private async Task PopulateAsync(HomeDepotReview review, IEnumerable<HomeDepotLine> lines, CancellationToken ct)
    {
        review.Candidates = []; review.LookupAt = DateTimeOffset.UtcNow;
        review.SuggestedJobId = ""; review.SuggestedJobNumber = ""; review.LookupMessage = "";
        review.LookupOperationId = review.OperationId; review.LookupReference = review.Reference.Trim(); review.LookupAddress = review.Address.Trim();
        if (review.OperationId is null) { review.LookupStatus = "OperationRequired"; review.LookupMessage = "Select the purchasing operation before searching Housecall Pro jobs."; return; }
        var operation = await OperationAsync(review.OperationId, ct);
        try
        {
            var references = lines.SelectMany(l => new[] { l.Value("Job Name"), l.Value("Project Name") })
                .Concat(new[] { review.Reference, review.Address }).Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (references.Count == 0)
            {
                review.LookupStatus = "ReferenceRequired";
                review.LookupMessage = "Enter a source job number, homeowner name or street address; the source reference is blank.";
                return;
            }
            var jobs = await hcp.GetSupplierJobsAsync(operation.Slug, ct);
            review.Candidates = HomeDepotJobMatcher.Find(references, jobs, operation.Id, operation.EffectiveDisplayName);
            var oversized = references.Any(x => x.Length > 2000);
            var top = review.Candidates.FirstOrDefault();
            var uniqueStrong = top is not null && top.Rank >= 90 && review.Candidates.Count(x => x.Rank == top.Rank) == 1;
            if (uniqueStrong && !oversized)
            {
                review.SuggestedJobId = top!.JobId; review.SuggestedJobNumber = top.JobNumber;
            }
            review.LookupStatus = oversized ? "IncompleteSourceReferences"
                : review.Candidates.Count == 0 ? "CompleteNoCandidate"
                : uniqueStrong ? "UniqueStrongCandidateRequiresReview"
                : review.Candidates.Count == 1 ? "WeakCandidateRequiresReview" : "MultipleCandidatesRequireReview";
            review.LookupMessage = oversized ? "A source reference exceeds the matching limit. Review the original and enter a shorter lookup reference; this is not a complete no-match result."
                : review.Candidates.Count == 0 ? "No candidate after checking whole job numbers, source street addresses, full homeowner names and whole first/last-name components in this operation's HCP API data."
                : uniqueStrong ? "One strongest candidate is suggested. Confirm homeowner, address and project before saving material attribution; no job assignment was made."
                : "Confirm the homeowner, full address and project. Name/address candidates can refer to multiple jobs; no candidate was assigned automatically.";
        }
        catch (Exception ex) when (ex is ArgumentException or HttpRequestException or JsonException or InvalidDataException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            review.LookupStatus = "IncompleteOrUnavailable";
            review.Candidates = []; review.SuggestedJobId = ""; review.SuggestedJobNumber = "";
            review.LookupMessage = "Housecall Pro lookup is incomplete or unavailable. This does not establish that no job matches.";
            // Never serialize API error bodies, URLs, private source data or credentials.
        }
    }

    public async Task LookupAsync(string id, HomeDepotReview input, long version, ClaimsPrincipal user, CancellationToken ct)
    {
        var actor = Administrator(user);
        if (input.Reference.Length > 2000 || input.Address.Length > 2000) throw new ArgumentException("Lookup reference/address exceeds 2000 characters.");
        var snapshot = await store.ReadAsync(actor, ct);
        var purchase = snapshot.Purchases.Single(x => x.Id == id);
        await PopulateAsync(input, purchase.ReportingLines, ct);
        await store.ChangeAsync(state =>
        {
            var current = state.Purchases.Single(x => x.Id == id);
            if (current.Version != version) throw new ArgumentException("This transaction changed. Reopen it before saving.");
            var before = Serialize(current.Review);
            // Lookup saves evidence only; it cannot submit financial edits or approve allocation.
            current.Review.Candidates = input.Candidates; current.Review.LookupAt = input.LookupAt;
            current.Review.LookupStatus = input.LookupStatus;
            current.Review.LookupOperationId = input.LookupOperationId;
            current.Review.LookupReference = input.LookupReference; current.Review.LookupAddress = input.LookupAddress;
            current.Review.LookupMessage = input.LookupMessage;
            current.Review.SuggestedJobId = input.SuggestedJobId; current.Review.SuggestedJobNumber = input.SuggestedJobNumber;
            current.Version++; current.ChangedAt = DateTimeOffset.UtcNow;
            state.Audit.Add(new(DateTimeOffset.UtcNow, actor, "Lookup", id, before, Serialize(current.Review), "Operation-scoped HCP lookup; no assignment made"));
            return Task.FromResult(true);
        }, ct);
    }

    public async Task ReviewAsync(string id, HomeDepotReview input, long version, ClaimsPrincipal user, CancellationToken ct)
    {
        var actor = Administrator(user);
        if (input.Reason.Trim().Length < 5 || input.Reason.Length > 2000) throw new ArgumentException("A review/correction reason of 5–2000 characters is required.");
        if (input.Status is not ("NeedsReview" or "Reporting" or "Excluded")) throw new ArgumentException("Invalid reporting status.");
        if (input.PaymentState is not ("Unknown" or "Paid" or "Unpaid" or "Refunded" or "Cancelled")) throw new ArgumentException("Invalid payment state.");
        if (input.PaymentState == "Unpaid") throw new ArgumentException("Home Depot purchases follow the prepaid-card policy and cannot establish an unpaid supplier liability. Leave settlement evidence unknown until reconciled.");
        if (input.TransactionType is not ("Unknown" or "Order" or "Purchase" or "Invoice" or "Return" or "Credit" or "Cancellation")) throw new ArgumentException("Invalid transaction type.");
        if (input.Currency != "USD") throw new ArgumentException("Only explicitly reviewed USD records are currently supported.");
        if (input.Allocations.Count > 100 || input.Conflicts.Count > 100 || input.Conflicts.Any(x => x.Length > 2000)
            || new[] { input.Reference, input.Address, input.CustomerId, input.HcpCompanyId, input.Payer, input.IdentityReason }.Any(x => x.Length > 2000)) throw new ArgumentException("Review fields exceed their supported limits.");
        foreach (var amount in new[] { input.ReceiptTotal, input.Tax, input.Shipping, input.Fees, input.Discounts }.Concat(input.Allocations.Select(x => (decimal?)x.Amount)))
            if (amount is { } value && (decimal.Round(value, 2) != value || Math.Abs(value) > 999999999m)) throw new ArgumentException("Financial amounts must use supported cents precision and range; values were not rounded.");
        var operation = input.OperationId is not null ? await OperationAsync(input.OperationId, ct) : null;
        if (input.Payer.Length > 0 && !accounting.Value.PayingEntities.ContainsKey(input.Payer)) throw new ArgumentException("Select a configured payer, or leave it unknown.");
        if (input.Allocations.Count > 0)
        {
            if (operation is null || input.ReceiptTotal is null) throw new ArgumentException("Allocations require a purchasing operation and a reviewed source total.");
            var jobs = await hcp.GetSupplierJobsAsync(operation.Slug, ct); // failure blocks assignment, not intake/reporting.
            if (input.Allocations.Select(x => x.JobId).Distinct().Count() != input.Allocations.Count || input.Allocations.Any(a => !jobs.Any(j => j.ExternalId == a.JobId && j.JobNumber == a.JobNumber))) throw new ArgumentException("Every allocation must identify a distinct validated job in the selected operation.");
            if (input.CustomerId.Length > 0 && input.Allocations.Any(a => !jobs.Any(j => j.ExternalId == a.JobId && j.CustomerId == input.CustomerId))) throw new ArgumentException("The selected customer must own every allocated HCP job. Leave customer unresolved for cross-customer reporting allocations.");
            if (input.Allocations.Sum(x => x.Amount) != input.ReceiptTotal || input.Allocations.Any(a => input.ReceiptTotal > 0 && a.Amount <= 0 || input.ReceiptTotal < 0 && a.Amount >= 0)) throw new ArgumentException("Job allocations must reconcile exactly to the source transaction total and retain its sign.");
        }
        input.Reviewer = actor; input.ReviewedAt = DateTimeOffset.UtcNow;
        // Candidates are always server generated, never trusted from edited JSON.
        input.Candidates = []; input.LookupStatus = "NotRequested"; input.LookupAt = null;
        input.SuggestedJobId = ""; input.SuggestedJobNumber = ""; input.LookupMessage = "";
        input.LookupOperationId = null; input.LookupReference = ""; input.LookupAddress = "";
        await store.ChangeAsync(state =>
        {
            var purchase = state.Purchases.Single(x => x.Id == id);
            if (purchase.Version != version) throw new ArgumentException("This transaction changed. Reopen it before saving.");
            if (input.PaymentState != "Unknown" && !state.Documents.Any(d => d.Id == input.PaymentEvidenceDocumentId && d.PurchaseId == id && d.Kind != "Csv")) throw new ArgumentException("Known settlement state requires an attached original and explicit review; CSV history alone does not prove payment state.");
            if (input.IdentityConfirmed && input.IdentityReason.Trim().Length < 5) throw new ArgumentException("Confirm transaction identity with a reason, including any duplicate/overlap resolution.");
            if (input.ReportingCsvDocumentId.Length > 0 && !purchase.Lines.Any(l => l.Sources.Any(s => s.DocumentId == input.ReportingCsvDocumentId))) throw new ArgumentException("Select a CSV original belonging to this transaction for reporting; raw alternatives remain retained.");
            if (input.ReceiptTotal is not null && !state.Documents.Any(d => d.PurchaseId == id && d.Kind != "Csv")) throw new ArgumentException("A receipt/invoice original is required to establish the transaction total. Extended Retail is not a receipt total.");
            var before = Serialize(purchase.Review);
            // Material attribution has its own job/quantity validation. The legacy
            // financial JSON editor cannot bypass it or erase approved line links.
            input.MaterialAllocations = purchase.Review.MaterialAllocations;
            purchase.Review = JsonSerializer.Deserialize<HomeDepotReview>(Serialize(input), HomeDepotHistoryText.Json)!;
            purchase.Version++; purchase.ChangedAt = DateTimeOffset.UtcNow;
            state.Audit.Add(new(DateTimeOffset.UtcNow, actor, "Reviewed", id, before, Serialize(input), input.Reason));
            return Task.FromResult(true);
        }, ct);
    }

    public async Task<object> FeedAsync(long afterRevision, bool accountingOnly, string actor, CancellationToken ct)
    {
        var state = await store.ReadAsync(actor, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var operations = await db.LocalOperations.AsNoTracking().Where(x => x.IsActive).ToDictionaryAsync(x => x.Id, ct);
        var items = new List<object>();
        // A full, bounded-account snapshot per revision avoids missing removals or policy changes.
        foreach (var purchase in state.Purchases)
        {
            var r = purchase.Review;
            var op = operations.GetValueOrDefault(r.OperationId ?? 0);
            var policy = new SupplierInvoicePayload();
            SupplierInvoiceAccounting.Assign(policy, op?.Slug, accounting.Value);
            // Prepaid materials never qualify for AP; card reconciliation is deferred.
            var eligible = false;
            if (accountingOnly && !eligible) continue;
            items.Add(new
            {
                purchase.Id, purchase.Version, purchase.ChangedAt, purchase.IdentityQuality,
                postingKey = "home-depot:" + Hash(state.AccountScope + ":" + purchase.IdentityKey),
                sourceIdentifiers = purchase.Lines.Select(l => new { date = l.Value("Date"), store = l.Value("Store Number"), register = l.Value("Register Number"), transaction = l.Value("Transaction ID"), order = l.Value("Order Number"), invoice = l.Value("Invoice Number") }).Distinct(),
                originals = state.Documents.Where(d => d.PurchaseId == purchase.Id || purchase.Lines.Any(l => l.Sources.Any(s => s.DocumentId == d.Id))).Select(d => new { d.Id, d.Kind, d.Sha256, d.Coverage, url = "/api/home-depot-history/documents/" + d.Id }),
                reviewed = r, lines = purchase.Lines,
                purchaseEntityCode = policy.PurchaseEntityCode,
                eligibleForAccountingReview = eligible,
                accountingHandoffStatus = eligible ? "ReadyForAccountingReview" : "NotEligible",
                accountingTreatment = "PrepaidCardMaterialsNeverPayable", createsAccountsPayable = false, cardReconciliationStatus = "Deferred",
                purchase.ExternalAccountingStatus, automaticExternalPosting = false
            });
        }
        return new { revision = state.Revision, requestedAfterRevision = afterRevision, snapshot = true, items, accountingReviewRequired = false, cardReconciliationStatus = "Deferred", createsAccountsPayable = false, automaticExternalPosting = false };
    }
}
