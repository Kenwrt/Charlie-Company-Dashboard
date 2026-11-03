using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Models;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class SupplierInvoiceService(
    IDbContextFactory<ApplicationDbContext> factory,
    IOptions<SupplierInvoiceOptions> options,
    SupplierInvoiceRouting routing,
    IWebHostEnvironment environment,
    ILogger<SupplierInvoiceService> logger)
{
    public const int MaxPdfBytes = 20 * 1024 * 1024;
    public const int MaxPayloadBytes = 512 * 1024;

    public async Task<SupplierInvoiceResult> StageAsync(byte[] pdf, string fileName,
        SupplierInvoicePayload payload, string actor, CancellationToken ct, bool refreshExisting = false)
    {
        var settings = options.Value;
        if (!settings.Enabled || settings.DecksDocksVendorId <= 0)
            throw new InvalidOperationException("Supplier invoice automation is not configured.");
        if (pdf.Length < 5 || pdf.Length > MaxPdfBytes || !pdf.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            throw new ArgumentException("A PDF of at most 20 MB is required.");
        GuardPayload(payload);
        var hash = Convert.ToHexString(SHA256.HashData(pdf));
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize invoice imports for this supplier, including different PDFs of the same invoice.
        await LockVendorAsync(db, settings.DecksDocksVendorId, ct);
        var vendor = await db.SupplyVendors.SingleOrDefaultAsync(x => x.Id == settings.DecksDocksVendorId && x.IsActive, ct);
        if (vendor is null || !vendor.Name.Contains("decks", StringComparison.OrdinalIgnoreCase)
            || !vendor.Name.Contains("docks", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Configure the existing Decks & Docks supplier before importing.");
        var document = await db.PriceImportDocuments.Include(x => x.Rows).SingleOrDefaultAsync(x => x.Sha256 == hash, ct);
        if (document is not null)
        {
            if (document.SupplyVendorId != vendor.Id)
                throw new ArgumentException("This document is already assigned to another supplier.");
            var prior = await db.SupplierInvoiceImports.SingleOrDefaultAsync(x => x.PriceImportDocumentId == document.Id, ct);
            if (prior is not null)
            {
                if (refreshExisting && prior.Status == "Needs Review" && prior.ReviewedAt is null && !string.IsNullOrWhiteSpace(payload.ExtractionVersion))
                {
                    var current = JsonNode.Parse(prior.PayloadJson)!.AsObject();
                    if (current["extractionVersion"]?.GetValue<string>() != payload.ExtractionVersion)
                    {
                        var incoming = JsonSerializer.SerializeToNode(payload, SupplierInvoicePayload.JsonOptions)!.AsObject();
                        var merged = SupplierInvoiceExtractionMerge.Merge(current, JsonNode.Parse(prior.OriginalPayloadJson)!.AsObject(), incoming, out var preserved);
                        var candidate = merged.Deserialize<SupplierInvoicePayload>(SupplierInvoicePayload.JsonOptions)!;
                        if (!candidate.LocationAssignmentConfirmed && candidate.LocationAssignmentSource != "AdministratorOverride")
                        {
                            var resolved = await routing.ResolveAsync(db, candidate.YourReference, ct);
                            merged["localOperationId"] = resolved.OperationId;
                            merged["locationAssignmentSource"] = resolved.Source;
                            merged["locationAssignmentReason"] = resolved.Reason;
                            merged["locationRequiresReview"] = resolved.RequiresReview;
                            candidate = merged.Deserialize<SupplierInvoicePayload>(SupplierInvoicePayload.JsonOptions)!;
                        }
                        var products = await LoadProductsAsync(db, vendor.Id, ct);
                        var errors = await PostingIssuesAsync(db, candidate, products, false, ct);
                        prior.PayloadJson = merged.ToJsonString(SupplierInvoicePayload.JsonOptions);
                        prior.ReviewReason = Limit(string.Join(" ", errors.Prepend("Extraction refreshed from the original PDF. Existing review edits were preserved.")), 500);
                        db.OperationalEvents.Add(new OperationalEvent
                        {
                            Environment = environment.EnvironmentName, Machine = System.Environment.MachineName,
                            Service = "CharlieCompanyDashboard", Module = "SupplierInvoices", CorrelationId = prior.CorrelationId,
                            Step = "ExtractionRefreshed", Status = "Needs Review", Message = "Pending invoice extraction refreshed without AP posting.",
                            MetadataJson = JsonSerializer.Serialize(new { actor, version = payload.ExtractionVersion, preservedFields = preserved })
                        });
                        await db.SaveChangesAsync(ct);
                        await transaction.CommitAsync(ct);
                        return new(prior.Id, prior.Status, prior.PayableInvoiceId, true, true);
                    }
                }
                return new(prior.Id, prior.Status, prior.PayableInvoiceId, true);
            }
        }
        else
        {
            var root = Path.GetFullPath(settings.StoragePath);
            Directory.CreateDirectory(root);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var path = Path.Combine(root, $"{Guid.NewGuid():N}.pdf");
            await File.WriteAllBytesAsync(path, pdf, ct);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            document = new PriceImportDocument
            {
                SupplyVendorId = vendor.Id, OriginalFileName = SafeFileName(fileName),
                StoragePath = path, Sha256 = hash, ContentType = "application/pdf", Status = "Invoice Review"
            };
            db.PriceImportDocuments.Add(document);
        }
        var json = JsonSerializer.Serialize(payload, SupplierInvoicePayload.JsonOptions);
        var assignment = await routing.ResolveAsync(db, payload.YourReference, ct);
        payload.LocalOperationId = assignment.OperationId;
        payload.PayingEntityCode = ""; // Payer is unknown until the reviewer identifies it.
        SupplierInvoiceAccounting.Assign(payload, (await db.LocalOperations.FindAsync([payload.LocalOperationId ?? 0], ct))?.Slug, settings);
        payload.LocationAssignmentSource = assignment.Source;
        payload.LocationAssignmentReason = assignment.Reason;
        payload.LocationRequiresReview = assignment.RequiresReview;
        // Incoming collector assertions cannot impersonate administrator review.
        payload.LocationAssignmentConfirmed = false;
        payload.LocationOverrideReason = "";
        var import = new SupplierInvoiceImport { PriceImportDocument = document,
            PayloadJson = JsonSerializer.Serialize(payload, SupplierInvoicePayload.JsonOptions), OriginalPayloadJson = json };
        db.SupplierInvoiceImports.Add(import);
        var items = await LoadProductsAsync(db, vendor.Id, ct);
        var issues = await PostingIssuesAsync(db, payload, items, false, ct);
        var duplicate = await FindExistingInvoiceAsync(db, vendor.Id, payload.InvoiceNumber, ct);
        if (duplicate is not null)
        {
            import.Status = "Duplicate Invoice";
            import.ReviewReason = "An AP invoice already uses this invoice number. The document was retained without changing AP or prices.";
        }
        // All supplier invoice AP postings require administrator review.
        else
            import.ReviewReason = Limit(string.Join(" ", issues.Prepend("Review the original invoice before posting.")), 500);
        AddAudit(db, import, "Received", actor);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Supplier invoice {ImportId} staged with status {Status} and correlation {CorrelationId}", import.Id, import.Status, import.CorrelationId);
        return new(import.Id, import.Status, import.PayableInvoiceId, false);
    }

    public async Task<SupplierInvoiceResult> ApproveAsync(int id, SupplierInvoicePayload payload,
        bool allowNewProducts, bool approvePrices, string actor, CancellationToken ct)
    {
        GuardPayload(payload);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Read the supplier first, then reload inside the supplier lock.
        var vendorId = await db.SupplierInvoiceImports.Where(x => x.Id == id)
            .Select(x => x.PriceImportDocument.SupplyVendorId).SingleAsync(ct);
        await LockVendorAsync(db, vendorId, ct);
        var import = await db.SupplierInvoiceImports.Include(x => x.PriceImportDocument).ThenInclude(x => x.Rows).SingleAsync(x => x.Id == id, ct);
        if (import.Status == "Posted") return new(import.Id, import.Status, import.PayableInvoiceId, true);
        if (import.Status != "Needs Review") throw new ArgumentException("Only an invoice awaiting review can be posted.");
        var savedCandidate = JsonSerializer.Deserialize<SupplierInvoicePayload>(import.PayloadJson, SupplierInvoicePayload.JsonOptions)!;
        if (savedCandidate.ExtractionVersion != payload.ExtractionVersion)
            throw new ArgumentException("This extraction was refreshed while the review was open. Keep a copy of your edits, then reload the review before posting.");
        if (!payload.LocationAssignmentConfirmed)
            throw new ArgumentException("Confirm the purchase-owning entity, purchasing location and payer information before posting.");
        var assignment = await routing.ResolveAsync(db, payload.YourReference, ct);
        var overridesLocation = assignment.RequiresReview || assignment.OperationId != payload.LocalOperationId;
        if (overridesLocation && string.IsNullOrWhiteSpace(payload.LocationOverrideReason))
            throw new ArgumentException("Explain the order, job or supplier record used to resolve the uncertain location.");
        payload.LocationAssignmentSource = overridesLocation ? "AdministratorOverride" : assignment.Source;
        payload.LocationAssignmentReason = overridesLocation ? payload.LocationOverrideReason.Trim() : assignment.Reason;
        payload.LocationRequiresReview = false;
        SupplierInvoiceAccounting.Assign(payload, (await db.LocalOperations.FindAsync([payload.LocalOperationId ?? 0], ct))?.Slug, options.Value);
        var items = await LoadProductsAsync(db, vendorId, ct);
        var issues = await PostingIssuesAsync(db, payload, items, allowNewProducts, ct);
        if (issues.Count > 0) throw new ArgumentException(string.Join(" ", issues));
        if (await FindExistingInvoiceAsync(db, vendorId, payload.InvoiceNumber, ct) is not null)
            throw new ArgumentException("An AP invoice already uses this supplier invoice number. No changes were made.");
        var priorPayload = JsonSerializer.Deserialize<SupplierInvoicePayload>(import.PayloadJson, SupplierInvoicePayload.JsonOptions);
        db.OperationalEvents.Add(new OperationalEvent
        {
            Environment = environment.EnvironmentName, Machine = System.Environment.MachineName,
            Service = "CharlieCompanyDashboard", Module = "SupplierInvoices", CorrelationId = import.CorrelationId,
            Step = "AssignmentReviewed", Status = "Approved", Message = "Supplier invoice payer and purchasing location reviewed.",
            MetadataJson = JsonSerializer.Serialize(new
            {
                actor, previousPayer = priorPayload?.PayingEntityCode, previousLocation = priorPayload?.LocalOperationId,
                payer = payload.PayingEntityCode, owner = payload.PurchaseEntityCode, policy = payload.AccountingPolicy,
                handoff = payload.AccountingHandoffStatus, location = payload.LocalOperationId, source = payload.LocationAssignmentSource
            })
        });
        await PostCoreAsync(db, import, payload, items, allowNewProducts, approvePrices, actor, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Supplier invoice {ImportId} posted with correlation {CorrelationId}", id, import.CorrelationId);
        return new(import.Id, import.Status, import.PayableInvoiceId, false);
    }

    public async Task RejectAsync(int id, string actor, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var vendorId = await db.SupplierInvoiceImports.Where(x => x.Id == id).Select(x => x.PriceImportDocument.SupplyVendorId).SingleAsync(ct);
        await LockVendorAsync(db, vendorId, ct);
        var import = await db.SupplierInvoiceImports.Include(x => x.PriceImportDocument).ThenInclude(x => x.Rows).SingleAsync(x => x.Id == id, ct);
        if (import.Status != "Needs Review") throw new ArgumentException("Only an invoice awaiting review can be rejected.");
        import.Status = "Rejected"; import.ReviewedBy = actor; import.ReviewedAt = DateTimeOffset.UtcNow;
        foreach (var row in import.PriceImportDocument.Rows.Where(x => x.ReviewStatus == "Pending")) row.ReviewStatus = "Rejected";
        AddAudit(db, import, "Rejected", actor);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task PostCoreAsync(ApplicationDbContext db, SupplierInvoiceImport import, SupplierInvoicePayload payload,
        List<VendorProduct> items, bool allowNewProducts, bool approvePrices, string actor, CancellationToken ct)
    {
        var vendorId = import.PriceImportDocument.SupplyVendorId;
        var date = payload.InvoiceDate!.Value;
        var rule = await db.PriceApprovalRules.AsNoTracking().Where(x => x.IsActive && (x.SupplyVendorId == vendorId || x.SupplyVendorId == null))
            .OrderByDescending(x => x.SupplyVendorId != null).FirstOrDefaultAsync(ct);
        var invoice = new PayableInvoice
        {
            SupplyVendorId = vendorId, LocalOperationId = payload.LocalOperationId!.Value,
            PayingEntityCode = string.IsNullOrWhiteSpace(payload.PayingEntityCode) ? null : payload.PayingEntityCode, YourReference = payload.YourReference.Trim(),
            InvoiceNumber = payload.InvoiceNumber.Trim().ToUpperInvariant(), JobNumber = payload.JobNumber.Trim(),
            InvoiceDate = date, DueDate = payload.DueDate!.Value, OriginalAmount = payload.Total,
            AmountPaid = payload.AmountPaid, Status = payload.AmountPaid == payload.Total ? "Paid" : payload.AmountPaid > 0 ? "Partially Paid" : "Open",
            Notes = $"Supplier invoice import {import.CorrelationId}. Tax {payload.Tax:F2}; freight {payload.Freight:F2}; discount {payload.Discount:F2}. Original document is available in supplier invoice review."
        };
        db.PayableInvoices.Add(invoice);
        var conflicts = payload.Lines.GroupBy(x => x.Sku.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Select(y => y.UnitPrice).Distinct().Count() > 1).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Rebuild only pending extraction rows after review edits; preserve earlier catalog decisions.
        var pending = import.PriceImportDocument.Rows.Where(x => x.ReviewStatus == "Pending").ToList();
        db.PriceImportRows.RemoveRange(pending);
        foreach (var row in pending) import.PriceImportDocument.Rows.Remove(row);
        foreach (var line in payload.Lines)
        {
            var sku = line.Sku.Trim();
            var item = items.SingleOrDefault(x => x.VendorSku.Equals(sku, StringComparison.OrdinalIgnoreCase));
            var isNew = item is null;
            if (item is null)
            {
                if (!allowNewProducts) throw new ArgumentException("New catalog products require review.");
                item = new VendorProduct
                {
                    SupplyVendorId = vendorId, VendorSku = sku, VendorDescription = line.Description.Trim(),
                    PurchaseUnit = line.PurchaseUnit.Trim(), PackageQuantity = line.PackageQuantity,
                    Product = new Product { Name = Limit(line.Description.Trim(), 160), UnitOfMeasure = line.PurchaseUnit.Trim() }
                };
                db.VendorProducts.Add(item); items.Add(item);
            }
            var current = item.Prices.Where(x => x.EffectiveDate <= date && (x.ExpirationDate == null || x.ExpirationDate >= date))
                .OrderByDescending(x => x.EffectiveDate).FirstOrDefault();
            var sameDate = item.Prices.FirstOrDefault(x => x.EffectiveDate == date);
            var previousDecision = import.PriceImportDocument.Rows.FirstOrDefault(x => x.VendorSku.Equals(sku, StringComparison.OrdinalIgnoreCase)
                && x.EffectiveDate == date && x.ProposedUnitPrice == line.UnitPrice && x.ReviewStatus != "Pending");
            var canApprove = !conflicts.Contains(sku) && (sameDate is null || sameDate.UnitPrice == line.UnitPrice)
                && previousDecision?.ReviewStatus != "Rejected"
                && (approvePrices || current?.UnitPrice == line.UnitPrice || (!isNew && WithinRule(rule, current?.UnitPrice, line.UnitPrice)));
            if (canApprove && sameDate is null)
            {
                item.Prices.Add(new VendorPrice { UnitPrice = line.UnitPrice, EffectiveDate = date, SourceType = "Invoice", SourceReference = invoice.InvoiceNumber });
                var ordered = item.Prices.OrderBy(x => x.EffectiveDate).ToList();
                for (var i = 0; i < ordered.Count; i++)
                    ordered[i].ExpirationDate = i + 1 < ordered.Count ? ordered[i + 1].EffectiveDate.AddDays(-1) : null;
            }
            if (previousDecision is null && !import.PriceImportDocument.Rows.Any(x => x.VendorSku.Equals(sku, StringComparison.OrdinalIgnoreCase)
                && x.EffectiveDate == date && x.ProposedUnitPrice == line.UnitPrice))
                import.PriceImportDocument.Rows.Add(new PriceImportRow
                {
                    VendorProduct = item, VendorSku = sku, Description = line.Description.Trim(), ProposedUnitPrice = line.UnitPrice,
                    EffectiveDate = date, MatchConfidence = 1m, ReviewStatus = canApprove ? "Approved" : "Pending"
                });
            invoice.Lines.Add(new InvoiceLine
            {
                VendorProduct = item, Quantity = line.Quantity, UnitPrice = line.UnitPrice, LineTotal = line.LineTotal,
                PriceReviewStatus = canApprove ? "Approved" : "Pending"
            });
        }
        import.PayloadJson = JsonSerializer.Serialize(payload, SupplierInvoicePayload.JsonOptions);
        import.PayableInvoice = invoice; import.Status = "Posted"; import.ReviewReason = null;
        import.ReviewedAt = DateTimeOffset.UtcNow; import.ReviewedBy = actor;
        import.PriceImportDocument.Status = "AP Invoice Posted";
        AddAudit(db, import, "Posted", actor);
    }

    private static bool WithinRule(PriceApprovalRule? rule, decimal? oldPrice, decimal newPrice)
    {
        if (rule is null || oldPrice is null || oldPrice <= 0 || rule.AutoApproveDollarThreshold < 0 || rule.AutoApprovePercentThreshold < 0) return false;
        var difference = Math.Abs(newPrice - oldPrice.Value);
        // Invoice extraction is not a trusted supplier API; never use the trusted-API switch here.
        return difference <= rule.AutoApproveDollarThreshold && difference / oldPrice.Value * 100 <= rule.AutoApprovePercentThreshold;
    }

    private async Task<List<string>> PostingIssuesAsync(ApplicationDbContext db, SupplierInvoicePayload p,
        List<VendorProduct> items, bool allowNewProducts, CancellationToken ct)
    {
        var issues = new List<string>();
        if (!p.HasSupportedPostingPrecision())
            issues.Add("Use cents for invoice amounts and at most four decimal places for quantities and unit prices before posting.");
        if (!string.IsNullOrWhiteSpace(p.PayingEntityCode) && !options.Value.PayingEntities.ContainsKey(p.PayingEntityCode)) issues.Add("Select a configured paying entity.");
        if (p.LocationRequiresReview) issues.Add(p.LocationAssignmentReason);
        if (string.IsNullOrWhiteSpace(p.PurchaseEntityCode)) issues.Add("Resolve the purchasing location to a configured purchase-owning entity.");
        if (string.IsNullOrWhiteSpace(p.InvoiceNumber)) issues.Add("Invoice number is required.");
        if (p.InvoiceDate is null || p.InvoiceDate > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))) issues.Add("A valid invoice date is required.");
        if (p.DueDate is null || p.DueDate < p.InvoiceDate) issues.Add("Confirm the due date.");
        if (p.LocalOperationId is null || !await db.LocalOperations.AnyAsync(x => x.Id == p.LocalOperationId && x.IsActive, ct)) issues.Add("Select an active local operation.");
        if (string.IsNullOrWhiteSpace(p.JobNumber)) issues.Add("Confirm the job number.");
        if (p.Currency != "USD") issues.Add("Only USD invoices are supported.");
        if (p.IsCreditNote) issues.Add("Credit notes must use the existing vendor-credit workflow.");
        if (!p.PaymentStateVerified) issues.Add("Confirm the amount already paid.");
        if (p.Total <= 0 || p.AmountPaid < 0 || p.AmountPaid > p.Total || p.Tax < 0 || p.Freight < 0 || p.Discount < 0) issues.Add("Confirm invoice totals and payments.");
        if (p.Lines.Count == 0) issues.Add("Invoice lines are required.");
        if (Math.Abs(p.Lines.Sum(x => x.LineTotal) + p.Tax + p.Freight - p.Discount - p.Total) > .01m) issues.Add("Lines, tax, freight and discount must reconcile to the invoice total.");
        foreach (var line in p.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Sku) || string.IsNullOrWhiteSpace(line.Description) || string.IsNullOrWhiteSpace(line.PurchaseUnit)
                || line.Quantity <= 0 || line.PackageQuantity <= 0 || line.UnitPrice < 0 || line.LineTotal < 0
                || Math.Abs(decimal.Round(line.Quantity * line.UnitPrice, 2, MidpointRounding.AwayFromZero) - line.LineTotal) > .01m)
                issues.Add("Each line needs a SKU, description, unit and reconciled positive quantity.");
            var item = items.SingleOrDefault(x => x.VendorSku.Equals(line.Sku.Trim(), StringComparison.OrdinalIgnoreCase));
            if (item is null && !allowNewProducts) issues.Add("New supplier SKUs require explicit catalog review.");
            if (item is not null && (!item.IsActive || item.PackageQuantity != line.PackageQuantity
                || !item.PurchaseUnit.Equals(line.PurchaseUnit.Trim(), StringComparison.OrdinalIgnoreCase)))
                issues.Add("An existing SKU has a different purchase unit or package quantity, or is inactive. Resolve its catalog mapping first.");
        }
        if (p.Lines.GroupBy(x => x.Sku.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Select(x => (x.PurchaseUnit.Trim().ToUpperInvariant(), x.PackageQuantity)).Distinct().Count() > 1))
            issues.Add("The same SKU has conflicting units or package quantities.");
        return issues.Distinct().ToList();
    }

    private static Task<List<VendorProduct>> LoadProductsAsync(ApplicationDbContext db, int vendorId, CancellationToken ct) =>
        db.VendorProducts.Include(x => x.Prices).Where(x => x.SupplyVendorId == vendorId).ToListAsync(ct);

    private static Task<PayableInvoice?> FindExistingInvoiceAsync(ApplicationDbContext db, int vendorId, string number, CancellationToken ct)
    {
        var normalized = number.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(normalized) ? Task.FromResult<PayableInvoice?>(null)
            : db.PayableInvoices.FirstOrDefaultAsync(x => x.SupplyVendorId == vendorId && x.InvoiceNumber.Trim().ToUpper() == normalized, ct);
    }

    private static async Task LockVendorAsync(ApplicationDbContext db, int vendorId, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({720000000000L + vendorId})", ct);

    private void AddAudit(ApplicationDbContext db, SupplierInvoiceImport import, string step, string actor) => db.OperationalEvents.Add(new OperationalEvent
    {
        Environment = environment.EnvironmentName, Machine = System.Environment.MachineName,
        Service = "CharlieCompanyDashboard", Module = "SupplierInvoices", CorrelationId = import.CorrelationId,
        Step = step, Status = import.Status, Message = "Supplier invoice workflow transition.",
        MetadataJson = JsonSerializer.Serialize(new { actor, import.Status })
    });

    private static void GuardPayload(SupplierInvoicePayload p)
    {
        if (p is null || p.Lines is null || p.Warnings is null || p.Lines.Count > 1000 || p.Warnings.Count > 100
            || p.ExtractionVersion is null || p.ExtractionVersion.Length > 80 || p.ExtractionEvidence is null
            || p.ExtractionEvidence.Count > 32 || p.ExtractionEvidence.Any(x => x.Key.Length > 80 || x.Value is null || x.Value.Length > 300)
            || p.YourReference is null || p.YourReference.Length > 500 || p.PayingEntityCode is null || p.PayingEntityCode.Length > 80
            || p.LocationAssignmentSource is null || p.LocationAssignmentSource.Length > 80
            || p.LocationAssignmentReason is null || p.LocationAssignmentReason.Length > 500
            || p.LocationOverrideReason is null || p.LocationOverrideReason.Length > 500
            || p.InvoiceNumber is null || p.InvoiceNumber.Length > 100 || p.JobNumber is null || p.JobNumber.Length > 100
            || p.Currency is null || p.Currency.Length > 10 || p.Warnings.Any(x => x is null || x.Length > 500)
            || p.Lines.Any(x => x is null || x.Sku is null || x.Sku.Length > 100 || x.Description is null || x.Description.Length > 300
                || x.PurchaseUnit is null || x.PurchaseUnit.Length > 40
                || Math.Abs(x.Quantity) > 1000000 || Math.Abs(x.UnitPrice) > 10000000 || Math.Abs(x.LineTotal) > 1000000000
                || Math.Abs(x.PackageQuantity) > 1000000)
            || Math.Abs(p.Total) > 1000000000 || Math.Abs(p.Tax) > 1000000000 || Math.Abs(p.Freight) > 1000000000
            || Math.Abs(p.Discount) > 1000000000 || Math.Abs(p.AmountPaid) > 1000000000)
            throw new ArgumentException("The invoice data exceeds supported limits.");
        if (System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(p, SupplierInvoicePayload.JsonOptions)) > MaxPayloadBytes)
            throw new ArgumentException("The invoice data exceeds supported limits.");
    }

    private static string SafeFileName(string name) => Limit(Path.GetFileName(name.Replace('\\', '/')), 260) is { Length: > 0 } value ? value : "invoice.pdf";
    private static string Limit(string text, int length) => text.Length <= length ? text : text[..length];
}

public sealed record SupplierInvoiceResult(int ImportId, string Status, int? PayableInvoiceId, bool Duplicate, bool ExtractionRefreshed = false);
