using System.Security.Claims;
using System.Text.Json;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Models;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

/// <summary>Read-only supplier projections plus audited Home Depot matching. Never posts AP or changes prices.</summary>
public sealed class JobMaterialsService(IDbContextFactory<ApplicationDbContext> factory,
    HomeDepotHistoryStore homeDepot, IOptions<HomeDepotHistoryOptions> settings, HousecallProDataService hcp)
{
    private static string Key(string? value) => (value ?? "").Trim().ToUpperInvariant();
    private static string Reference(HomeDepotLine line) => HomeDepotHistoryText.Normalize(
        string.IsNullOrWhiteSpace(line.Value("Job Name")) ? line.Value("Project Name") : line.Value("Job Name"));
    private static string Reason(string value) => value.Trim().Length is >= 5 and <= 2000 ? value.Trim()
        : throw new ArgumentException("Provide a review reason of 5–2000 characters.");

    public async Task<JobMaterialsReport> ReportAsync(int operationId, string jobNumber, ClaimsPrincipal user, CancellationToken ct)
    {
        var actor = HomeDepotHistoryService.Administrator(user);
        if (jobNumber.Trim().Length is 0 or > 100) throw new ArgumentException("Enter a job number.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var operation = await db.LocalOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == operationId && x.IsActive, ct)
            ?? throw new ArgumentException("Select an active operation.");
        var number = Key(jobNumber);
        var jobs = await db.HousecallProJobs.AsNoTracking().Where(x => x.LocalOperationId == operationId
            && x.JobNumber != null && x.JobNumber.Trim().ToUpper() == number).ToListAsync(ct);
        if (jobs.Count != 1) throw new ArgumentException(jobs.Count == 0
            ? "Job not found in the operation's synchronized Housecall Pro jobs. No name-based assignment was made."
            : "This operation has multiple jobs with that number. Resolve the job identity before reporting.");
        var job = jobs[0];
        var report = new JobMaterialsReport { OperationId = operation.Id, Operation = operation.EffectiveDisplayName,
            JobId = job.ExternalId, JobNumber = job.JobNumber!, Customer = job.CustomerName ?? "", JobLastSyncedAt = job.LastSyncedAt };
        var imports = await db.SupplierInvoiceImports.AsNoTracking().Include(x => x.PriceImportDocument).ThenInclude(x => x.SupplyVendor)
            .Where(x => x.Status != "Rejected" && x.Status != "Duplicate Invoice").ToListAsync(ct);
        HomeDepotHistoryState? state = null;
        if (settings.Value.Enabled)
        {
            try { state = await homeDepot.ReadAsync(actor, ct); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException)
            { report.Coverage.Add("Home Depot source storage is unavailable or invalid. Its costs are missing, not zero; other supplier sources are still shown."); }
        }
        var vendorIds = imports.Select(x => x.PriceImportDocument.SupplyVendorId).ToHashSet();
        if (state?.SupplyVendorId is int hdVendor) vendorIds.Add(hdVendor);
        var catalog = await db.VendorProducts.AsNoTracking().Include(x => x.Product)
            .Where(x => vendorIds.Contains(x.SupplyVendorId) && x.IsActive && x.Product.IsActive).ToListAsync(ct);

        (VendorProduct? Item, string Status) Catalog(int? vendorId, string sku, string internet = "")
        {
            if (vendorId is null) return (null, "Supplier link required");
            var manual = state?.CatalogLinks.SingleOrDefault(x => x.SupplyVendorId == vendorId && Key(x.SourceSku) == Key(sku));
            if (manual is not null)
            {
                var linked = catalog.SingleOrDefault(x => x.Id == manual.VendorProductId && x.SupplyVendorId == vendorId);
                return (linked, linked is null ? "Reviewed catalog link unavailable" : "Reviewed supplier SKU crosswalk");
            }
            var keys = new[] { Key(sku), Key(internet) }.Where(x => x.Length > 0).ToHashSet();
            var candidates = catalog.Where(x => x.SupplyVendorId == vendorId && keys.Contains(Key(x.VendorSku))).ToList();
            return candidates.Count == 1 ? (candidates[0], "Unique exact supplier SKU match; no price update")
                : (null, candidates.Count == 0 ? "Catalog match required" : "Conflicting SKU candidates; review required");
        }

        if (state is null && !settings.Value.Enabled) report.Coverage.Add("Home Depot intake is disabled. Archived CSVs are not automatically loaded into the dashboard.");
        if (state is not null)
        {
            report.HomeDepotRevision = state.Revision; report.HomeDepotSupplyVendorId = state.SupplyVendorId;
            report.HomeDepotReferenceLinks = state.JobReferenceLinks.ToList();
            var purchases = state.Purchases.Where(p => p.Review.Status != "Excluded").ToList();
            report.HomeDepotReferences = purchases.SelectMany(p => p.ReportingLines).Where(l => Reference(l).Length > 0)
                .GroupBy(Reference).OrderBy(g => g.Key).Select(g => new HomeDepotSourceReference(g.Key, g.Count())).ToList();
            var supplier = state.SupplyVendorId is int id ? await db.SupplyVendors.AsNoTracking().Where(x => x.Id == id).Select(x => x.Name).SingleOrDefaultAsync(ct) : null;
            var unassigned = 0;
            foreach (var purchase in purchases)
            foreach (var line in purchase.ReportingLines)
            {
                var all = purchase.Review.MaterialAllocations.Where(a => a.LineId == line.Id).ToList();
                var qty = line.Amount("Quantity"); var amount = line.Amount("Extended Retail (before discount)");
                if (all.Count > 0 && (qty is null || amount is null || all.Sum(a => a.Quantity) != qty || all.Sum(a => a.ReportedAmount) != amount))
                { unassigned++; continue; } // Retained source changed: old allocations cannot be silently reused.
                if (all.Count == 0 && line.Date is DateOnly date)
                {
                    var links = state.JobReferenceLinks.Where(x => x.SourceReference == Reference(line)
                        && date >= x.FromDate && date <= x.ToDate).ToList();
                    if (links.Count == 1 && (purchase.Review.OperationId is null || purchase.Review.OperationId == links[0].OperationId)
                        && qty is not null && amount is not null)
                    {
                        var link = links[0];
                        all.Add(new(line.Id, link.OperationId, link.JobId, link.JobNumber, qty.Value, amount.Value));
                    }
                }
                if (all.Count == 0) { unassigned++; continue; }
                foreach (var allocation in all.Where(a => a.OperationId == operation.Id && a.JobId == job.ExternalId && Key(a.JobNumber) == number))
                {
                    var match = Catalog(state.SupplyVendorId, line.Value("SKU Number"), line.Value("Internet SKU"));
                    var note = "Purchased/allocated, not proof of installed consumption. Quantity remains source purchase units; packaging conversion is unverified.";
                    if (allocation.ReportedAmount < 0) note += " Negative source amount: return/credit classification needs review.";
                    if (purchase.Review.Conflicts.Count > 0) note += " Source transaction has unresolved conflicts.";
                    if (line.Occurrence > 1) note += $" Repeated identical source line occurrence {line.Occurrence} retained; not automatically removed.";
                    report.Lines.Add(new("HomeDepot", purchase.Id, line.Id, line.Sources.FirstOrDefault()?.DocumentId ?? "", purchase.Id,
                        supplier ?? "Home Depot (supplier link pending)", state.SupplyVendorId, line.Date,
                        line.Value("SKU Number"), line.Value("SKU Description"), allocation.Quantity, "Source purchase units (unverified)",
                        allocation.ReportedAmount, "USD", "Reported retail before discount; not settled card total", "Prepaid credit card — never AP",
                        purchase.Review.MaterialAllocations.Any(a => a.LineId == line.Id) ? "Reviewed line allocation" : "Reviewed source reference + date range",
                        match.Item?.Id, match.Item?.ProductId, match.Status,
                        purchase.IdentityQuality == "Unresolved" || purchase.Review.Conflicts.Count > 0 || line.Date is null || allocation.ReportedAmount < 0 || line.Occurrence > 1 || match.Item is null, note));
                }
            }
            report.Coverage.Add($"Home Depot: {unassigned} unassigned/conflicting source lines across the whole account; some may belong to this job. Only one retained CSV snapshot per transaction is counted.");
        }

        // Read the source import once; never add its posted AP invoice as another purchase.
        var posted = new HashSet<int>(); var ambiguous = 0; var unreadable = 0;
        foreach (var import in imports.OrderByDescending(x => x.ReviewedAt ?? x.CreatedAt))
        {
            if (HomeDepotHistoryText.IsHomeDepotSupplier(import.PriceImportDocument.SupplyVendor.Name)
                || HomeDepotHistoryText.IsHomeDepotSupplier(import.PriceImportDocument.SupplyVendor.LegalName)) continue;
            SupplierInvoicePayload? payload;
            try { payload = JsonSerializer.Deserialize<SupplierInvoicePayload>(import.PayloadJson, SupplierInvoicePayload.JsonOptions); }
            catch (JsonException) { unreadable++; continue; }
            if (payload is null || payload.LocalOperationId != operation.Id) continue;
            if (import.PayableInvoiceId is int invoice && !posted.Add(invoice)) continue;
            var numbers = payload.MatchedJobNumbers.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Key).Distinct().ToList();
            if (numbers.Count > 1) { ambiguous++; continue; }
            if (payload.JobUnassigned || Key(payload.JobNumber) != number || numbers.Count == 1 && numbers[0] != number) continue;
            for (var i = 0; i < payload.Lines.Count; i++)
            {
                var line = payload.Lines[i]; var match = Catalog(import.PriceImportDocument.SupplyVendorId, line.Sku);
                report.Lines.Add(new("SupplierInvoice", import.Id.ToString(), $"{import.Id}:{i + 1}", import.PriceImportDocumentId.ToString(), "",
                    import.PriceImportDocument.SupplyVendor.Name, import.PriceImportDocument.SupplyVendorId,
                    payload.InvoiceDate, line.Sku, line.Description, line.Quantity, line.PurchaseUnit,
                    line.LineTotal, payload.Currency, "Supplier invoice line amount; excludes document-level adjustments",
                    payload.IsCreditNote ? "Supplier credit; existing workflow" : "Supplier invoice; existing payment workflow",
                    "Exact job number in selected operation; source review status retained", match.Item?.Id, match.Item?.ProductId, match.Status,
                    import.ReviewedAt is null || !payload.ExtractionComplete || payload.PostingConflicts.Count > 0 || payload.LocationRequiresReview || match.Item is null,
                    $"Import status: {import.Status}. Catalog prices are not used as historical purchase costs. Document tax/freight is not silently allocated."));
            }
        }
        report.Coverage.Add($"Supplier invoices: includes reviewed and clearly flagged provisional imports; excludes rejected/duplicate imports and {ambiguous} multi-job documents without item-level allocations. {unreadable} unreadable imports require review.");
        report.Coverage.Add("Sources currently supported: Home Depot history and supplier invoice imports (including Decks & Docks). Other supplier data needs an adapter; missing sources are not evidence of zero purchases. Purchases are not actual consumption.");
        report.Lines = report.Lines.OrderBy(x => x.PurchaseDate ?? DateOnly.MaxValue).ThenBy(x => x.Supplier).ThenBy(x => x.SourceLineId).ToList();
        report.Subtotals = report.Lines.GroupBy(x => new { x.Supplier, x.Currency, x.AmountBasis }).Select(g =>
            new JobMaterialSubtotal(g.Key.Supplier, g.Key.Currency, g.Key.AmountBasis, g.Sum(x => x.Amount ?? 0), g.Count())).ToList();
        var versions = await db.QuoteVersions.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.QuoteCase.LocalOperationId == operationId && x.QuoteCase.HousecallProJobId == job.ExternalId)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).ToListAsync(ct);
        foreach (var version in versions)
        {
            var materials = new List<JobEstimatedMaterial>();
            var note = "Saved selected options; planned quantity includes the saved waste allowance. This is the saved version, not a reconstructed estimate at purchase time.";
            try
            {
                if (version.OptionsJson is not null)
                {
                    var options = EstimateOptions.Read(version.OptionsJson);
                    foreach (var material in options.Selected.SelectMany(x => x.Materials))
                        materials.Add(new(material.VendorName, material.VendorSku ?? "", material.Description,
                            material.Unit, material.Quantity, material.Quantity * (1 + material.WastePercent / 100m), material.ExtendedCost));
                    if (!options.HasCalculatedCosts) note += " Selected options are incomplete or their calculations are not current.";
                }
                else
                {
                    materials.AddRange(version.Lines.OrderBy(x => x.SortOrder).Select(x => new JobEstimatedMaterial(
                        "Unspecified supplier", "", x.Description, x.Unit, x.Quantity,
                        x.Quantity * (1 + x.WastePercent / 100m), decimal.Round(x.Quantity * x.MaterialUnitCost * (1 + x.WastePercent / 100m), 2))));
                    note = "Legacy saved estimate lines have no supplier SKU identity; they require manual comparison and may contain task-level material allowances.";
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
            {
                materials.Clear(); note = "The saved material list could not be read. This is missing estimate evidence, not a zero estimate.";
            }
            report.Estimates.Add(new(version.Id, version.QuoteCaseId, version.VersionNumber, version.Status,
                version.CreatedAt, version.ApprovedAt, materials, note));
        }
        return report;
    }

    public static List<JobMaterialComparison> Compare(JobMaterialsReport report, JobMaterialEstimate estimate)
    {
        // Keep supplier SKU and purchase-unit semantics intact. Catalog identity alone
        // does not prove that historical packs and estimator units are interchangeable.
        var result = new List<JobMaterialComparison>();
        var matched = new HashSet<JobMaterialLine>();
        foreach (var group in estimate.Materials.GroupBy(x => new { Supplier = Key(x.Supplier), Sku = Key(x.Sku), Unit = Key(x.Unit) }))
        {
            var first = group.First();
            var candidates = group.Key.Sku.Length == 0 ? [] : report.Lines.Where(x =>
                Key(x.Supplier) == group.Key.Supplier && Key(x.SupplierSku) == group.Key.Sku).ToList();
            var comparable = candidates.Count > 0 && candidates.All(x => Key(x.PurchaseUnit) == group.Key.Unit && !x.NeedsReview);
            if (comparable) foreach (var line in candidates) matched.Add(line);
            result.Add(new(first.Supplier, first.Sku, string.Join("; ", group.Select(x => x.Description).Distinct()), first.Unit,
                group.Sum(x => x.PlannedQuantity), comparable ? candidates.Sum(x => x.Quantity) : null,
                group.Sum(x => x.Cost), comparable ? "Exact supplier / SKU / unit; net purchased quantity"
                    : group.Key.Sku.Length == 0 ? "No saved supplier SKU; manual comparison required"
                    : candidates.Count > 0 ? "Purchase found; units, packaging or source review prevents quantity comparison"
                    : "No attributed purchase match; check coverage, substitutions and other suppliers"));
        }
        foreach (var group in report.Lines.Where(x => !matched.Contains(x)).GroupBy(x => new { x.Supplier, x.SupplierSku, x.PurchaseUnit }))
            result.Add(new(group.Key.Supplier, group.Key.SupplierSku, string.Join("; ", group.Select(x => x.Description).Distinct()),
                group.Key.PurchaseUnit, 0, null, 0, "Purchase requires comparison — absent from estimate or not safely matched; see source lines"));
        return result;
    }

    private async Task ValidateJobAsync(int operationId, string jobId, string jobNumber, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var op = await db.LocalOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == operationId && x.IsActive, ct)
            ?? throw new ArgumentException("Select an active operation.");
        var jobs = await hcp.GetSupplierJobsAsync(op.Slug, ct);
        if (!jobs.Any(j => j.ExternalId == jobId && Key(j.JobNumber) == Key(jobNumber)))
            throw new ArgumentException("The selected job ID/number was not confirmed in this operation's Housecall Pro account.");
    }

    public async Task LinkReferenceAsync(string reference, DateOnly from, DateOnly to, int operationId,
        string jobId, string jobNumber, long revision, string reason, ClaimsPrincipal user, CancellationToken ct)
    {
        var actor = HomeDepotHistoryService.Administrator(user); reason = Reason(reason);
        var key = HomeDepotHistoryText.Normalize(reference);
        if (key.Length is 0 or > 500 || from > to) throw new ArgumentException("Select a source reference and valid date range.");
        await ValidateJobAsync(operationId, jobId, jobNumber, ct);
        await homeDepot.ChangeAsync(state =>
        {
            if (state.Revision != revision) throw new ArgumentException("Purchase data changed. Refresh the report before saving.");
            if (!state.Purchases.SelectMany(p => p.ReportingLines).Any(l => Reference(l) == key)) throw new ArgumentException("Source reference not found.");
            if (state.JobReferenceLinks.Any(x => x.SourceReference == key && from <= x.ToDate && to >= x.FromDate))
                throw new ArgumentException("This reference already has an overlapping reviewed date range. Use line-specific allocations or a non-overlapping range.");
            var link = new HomeDepotJobReferenceLink(key, operationId, jobId, jobNumber, from, to, actor, DateTimeOffset.UtcNow, reason);
            state.JobReferenceLinks.Add(link);
            state.Audit.Add(new(DateTimeOffset.UtcNow, actor, "MaterialReferenceLinked", null, "", JsonSerializer.Serialize(link, HomeDepotHistoryText.Json), reason));
            return Task.FromResult(true);
        }, ct);
    }

    public async Task SelectSupplierAsync(int vendorId, long revision, string reason, ClaimsPrincipal user, CancellationToken ct)
    {
        var actor = HomeDepotHistoryService.Administrator(user); reason = Reason(reason);
        await using var db = await factory.CreateDbContextAsync(ct);
        var vendor = await db.SupplyVendors.AsNoTracking().SingleOrDefaultAsync(x => x.Id == vendorId && x.IsActive, ct);
        if (vendor is null || !(HomeDepotHistoryText.IsHomeDepotSupplier(vendor.Name) || HomeDepotHistoryText.IsHomeDepotSupplier(vendor.LegalName)))
            throw new ArgumentException("Select the existing active Home Depot supplier. No supplier is created automatically.");
        await homeDepot.ChangeAsync(state =>
        {
            if (state.Revision != revision) throw new ArgumentException("Purchase data changed. Refresh first.");
            if (state.CatalogLinks.Count > 0 && state.SupplyVendorId != vendorId) throw new ArgumentException("Existing reviewed SKU crosswalks require resolution before changing supplier identity.");
            var before = state.SupplyVendorId?.ToString() ?? ""; state.SupplyVendorId = vendorId;
            state.Audit.Add(new(DateTimeOffset.UtcNow, actor, "MaterialSupplierLinked", null, before, vendorId.ToString(), reason));
            return Task.FromResult(true);
        }, ct);
    }

    public async Task UnlinkReferenceAsync(HomeDepotJobReferenceLink link, long revision, string reason, ClaimsPrincipal user, CancellationToken ct)
    {
        var actor = HomeDepotHistoryService.Administrator(user); reason = Reason(reason);
        await homeDepot.ChangeAsync(state =>
        {
            if (state.Revision != revision) throw new ArgumentException("Purchase data changed. Refresh first.");
            var current = state.JobReferenceLinks.SingleOrDefault(x => x == link) ?? throw new ArgumentException("Reference mapping changed. Refresh first.");
            state.JobReferenceLinks.Remove(current);
            state.Audit.Add(new(DateTimeOffset.UtcNow, actor, "MaterialReferenceUnlinked", null,
                JsonSerializer.Serialize(current, HomeDepotHistoryText.Json), "", reason));
            return Task.FromResult(true);
        }, ct);
    }

    public async Task LinkCatalogAsync(string sourceSku, string catalogSku, long revision, string reason, ClaimsPrincipal user, CancellationToken ct)
    {
        var actor = HomeDepotHistoryService.Administrator(user); reason = Reason(reason);
        if (sourceSku.Trim().Length is 0 or > 100 || catalogSku.Trim().Length is 0 or > 100) throw new ArgumentException("Enter both source and catalog SKUs.");
        var snapshot = await homeDepot.ReadAsync(actor, ct);
        if (snapshot.Revision != revision + 1 || snapshot.SupplyVendorId is not int vendorId) throw new ArgumentException("Refresh and select the existing Home Depot supplier first.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var sku = Key(catalogSku);
        var targets = await db.VendorProducts.AsNoTracking().Where(x => x.SupplyVendorId == vendorId && x.IsActive
            && x.Product.IsActive && x.VendorSku.Trim().ToUpper() == sku).ToListAsync(ct);
        if (targets.Count != 1) throw new ArgumentException("Catalog SKU must identify exactly one active item for the selected supplier.");
        await homeDepot.ChangeAsync(state =>
        {
            if (state.Revision != snapshot.Revision) throw new ArgumentException("Purchase data changed. Refresh first.");
            if (!state.Purchases.SelectMany(p => p.ReportingLines).Any(l => Key(l.Value("SKU Number")) == Key(sourceSku))) throw new ArgumentException("Source SKU not found.");
            var before = state.CatalogLinks.SingleOrDefault(x => x.SupplyVendorId == vendorId && Key(x.SourceSku) == Key(sourceSku));
            var link = new HomeDepotCatalogLink(sourceSku.Trim(), vendorId, targets[0].Id, actor, DateTimeOffset.UtcNow, reason);
            if (before is not null) state.CatalogLinks.Remove(before);
            state.CatalogLinks.Add(link);
            state.Audit.Add(new(DateTimeOffset.UtcNow, actor, "MaterialCatalogLinked", null, JsonSerializer.Serialize(before, HomeDepotHistoryText.Json), JsonSerializer.Serialize(link, HomeDepotHistoryText.Json), reason));
            return Task.FromResult(true);
        }, ct);
    }

    public async Task AllocateLinesAsync(string purchaseId, List<HomeDepotMaterialAllocation> allocations, long version,
        string reason, ClaimsPrincipal user, CancellationToken ct)
    {
        var actor = HomeDepotHistoryService.Administrator(user); reason = Reason(reason);
        if (allocations.Count > 1000) throw new ArgumentException("Too many line allocations.");
        foreach (var group in allocations.GroupBy(a => new { a.OperationId, a.JobId, a.JobNumber }))
            await ValidateJobAsync(group.Key.OperationId, group.Key.JobId, group.Key.JobNumber, ct);
        await homeDepot.ChangeAsync(state =>
        {
            var purchase = state.Purchases.Single(x => x.Id == purchaseId);
            if (purchase.Version != version) throw new ArgumentException("Purchase changed. Reopen it before saving.");
            foreach (var group in allocations.GroupBy(x => x.LineId))
            {
                var line = purchase.ReportingLines.SingleOrDefault(l => l.Id == group.Key) ?? throw new ArgumentException("Select a line in the current reporting snapshot.");
                var qty = line.Amount("Quantity"); var amount = line.Amount("Extended Retail (before discount)");
                if (qty is null || qty == 0 || amount is null || group.Sum(x => x.Quantity) != qty || group.Sum(x => x.ReportedAmount) != amount
                    || group.Any(x => x.Quantity == 0 || qty > 0 && x.Quantity < 0 || qty < 0 && x.Quantity > 0
                        || decimal.Round(x.Quantity, 4) != x.Quantity || decimal.Round(x.ReportedAmount, 2) != x.ReportedAmount
                        || amount > 0 && x.ReportedAmount < 0 || amount < 0 && x.ReportedAmount > 0 || amount == 0 && x.ReportedAmount != 0)
                    || group.Select(x => (x.OperationId, x.JobId)).Distinct().Count() != group.Count())
                    throw new ArgumentException("Split allocations must exactly conserve source quantity and amount, keep its sign, and identify distinct jobs. Receipt/payment evidence is not required for provisional material attribution.");
            }
            var before = JsonSerializer.Serialize(purchase.Review.MaterialAllocations, HomeDepotHistoryText.Json);
            purchase.Review.MaterialAllocations = allocations; purchase.Version++; purchase.ChangedAt = DateTimeOffset.UtcNow;
            state.Audit.Add(new(DateTimeOffset.UtcNow, actor, "MaterialLinesAllocated", purchaseId, before, JsonSerializer.Serialize(allocations, HomeDepotHistoryText.Json), reason));
            return Task.FromResult(true);
        }, ct);
    }
}
