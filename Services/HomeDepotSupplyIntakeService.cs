using System.Security.Claims;
using System.Text.Json;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed record HomeDepotSkuReview(string Sku, string Description, int PurchaseRows, string Status, bool CanAdd);
public sealed class HomeDepotSupplyIntakeService(IDbContextFactory<ApplicationDbContext> factory,
    HomeDepotHistoryStore store, HousecallProDataService hcp, IWebHostEnvironment environment)
{
    private static string Sku(string value) => value.Trim().ToUpperInvariant();
    public static string Reference(HomeDepotLine line) => HomeDepotHistoryText.Normalize(string.IsNullOrWhiteSpace(line.Value("Job Name")) ? line.Value("Project Name") : line.Value("Job Name"));
    private static List<HomeDepotSkuReview> Review(HomeDepotHistoryState state, List<VendorProduct> catalog)
    {
        return state.Purchases.Where(p => p.Review.Status != "Excluded").SelectMany(p => p.ReportingLines)
            .GroupBy(l => Sku(l.Value("SKU Number"))).Select(group => {
                var descriptions = group.Select(l => l.Value("SKU Description").Trim()).Where(d => d.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var existing = catalog.Where(v => Sku(v.VendorSku) == group.Key).ToList();
                var status = group.Key.Length is 0 or > 100 ? "Review: missing or invalid SKU"
                    : existing.Count > 1 ? "Duplicate catalog SKU — review later"
                    : descriptions.Count != 1 ? "Conflicting or missing descriptions — review later"
                    : descriptions[0].Length > 300 ? "Description too long — review later"
                    : existing.Count == 1 ? !existing[0].IsActive ? "Existing inactive SKU — review later"
                    : !string.Equals((existing[0].VendorDescription ?? existing[0].Product.Name).Trim(), descriptions[0], StringComparison.OrdinalIgnoreCase)
                        ? "Existing SKU description differs — review later" : "Already in supplies — skipped"
                    : "Ready to add";
                return new HomeDepotSkuReview(group.Key, descriptions.FirstOrDefault() ?? "", group.Count(), status, status == "Ready to add");
            }).OrderBy(r => r.Sku).ToList();
    }
    private static async Task<SupplyVendor> Vendor(ApplicationDbContext db, int id, CancellationToken ct)
    {
        var vendor = await db.SupplyVendors.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id && v.IsActive, ct);
        if (vendor is null || !(HomeDepotHistoryText.IsHomeDepotSupplier(vendor.Name) || HomeDepotHistoryText.IsHomeDepotSupplier(vendor.LegalName)))
            throw new ArgumentException("Select the existing Home Depot supplier.");
        return vendor;
    }
    public async Task<List<HomeDepotSkuReview>> PreviewAsync(HomeDepotHistoryState state, int vendorId, ClaimsPrincipal user, CancellationToken ct)
    {
        HomeDepotHistoryService.Administrator(user);
        await using var db = await factory.CreateDbContextAsync(ct); await Vendor(db, vendorId, ct);
        return Review(state, await db.VendorProducts.AsNoTracking().Include(v => v.Product).Where(v => v.SupplyVendorId == vendorId).ToListAsync(ct));
    }
    public async Task<int> AddAsync(int vendorId, ClaimsPrincipal user, CancellationToken ct)
    {
        var actor = HomeDepotHistoryService.Administrator(user);
        // The source lock prevents changes to the reporting snapshot during this intake.
        return await store.ChangeAsync(async state => {
            if (state.SupplyVendorId is int previous && previous != vendorId) throw new ArgumentException("The history is already connected to a different supplier. Resolve that link first.");
            await using var db = await factory.CreateDbContextAsync(ct); await Vendor(db, vendorId, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({720000000000L + vendorId})", ct);
            var catalog = await db.VendorProducts.Include(v => v.Product).Where(v => v.SupplyVendorId == vendorId).ToListAsync(ct);
            var rows = Review(state, catalog); var additions = rows.Where(r => r.CanAdd).ToList();
            foreach (var row in additions)
            {
                var product = new Product { Name = row.Description[..Math.Min(row.Description.Length, 160)], UnitOfMeasure = "Supplier unit" };
                db.VendorProducts.Add(new VendorProduct {
                    SupplyVendorId = vendorId, Product = product, VendorSku = row.Sku, VendorDescription = row.Description,
                    PurchaseUnit = "Supplier unit", PackageQuantity = 1,
                    PackagingVersions = [new VendorPackagingVersion { EffectiveDate = new DateOnly(1900, 1, 1), PurchaseUnit = "Supplier unit", PackageQuantity = null, IsConfirmed = false, Evidence = "Purchase-history intake; package size not established." }]
                });
            }
            db.OperationalEvents.Add(new OperationalEvent {
                Environment = environment.EnvironmentName, Machine = System.Environment.MachineName, Service = "CharlieCompanyDashboard", Module = "HomeDepotSupplies",
                CorrelationId = Guid.NewGuid().ToString("N"), Step = "SupplyIntake", Status = "Completed", Message = "Added missing Home Depot supplier SKUs; existing SKUs retained.",
                MetadataJson = JsonSerializer.Serialize(new { actor, vendorId, added = additions.Count, skipped = rows.Count - additions.Count })
            });
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            state.SupplyVendorId = vendorId;
            state.Audit.Add(new(DateTimeOffset.UtcNow, actor, "SupplyIntake", null, "", JsonSerializer.Serialize(new { vendorId, added = additions.Count }), "Owner-requested SKU intake; no stock, price, payable or packaging conversion changes."));
            return additions.Count;
        }, ct);
    }
    public async Task ConfirmCustomerAsync(string reference, int operationId, string customerId, ClaimsPrincipal user, CancellationToken ct)
    {
        var actor = HomeDepotHistoryService.Administrator(user);
        await using var db = await factory.CreateDbContextAsync(ct);
        var operation = await db.LocalOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == operationId && o.IsActive, ct) ?? throw new ArgumentException("Operation not found.");
        var jobs = await hcp.GetSupplierJobsAsync(operation.Slug, ct);
        var customer = jobs.FirstOrDefault(j => j.CustomerId == customerId && customerId.Length > 0) ?? throw new ArgumentException("Customer could not be confirmed in HCP. Refresh matching first.");
        var key = HomeDepotHistoryText.Normalize(reference);
        await store.ChangeAsync(state => {
            var purchases = state.Purchases.Where(p => p.ReportingLines.Any(l => Reference(l) == key)).ToList();
            if (!purchases.SelectMany(p => p.Review.Candidates).Any(c => c.OperationId == operationId && c.CustomerId == customerId)) throw new ArgumentException("Select a proposed customer from this group.");
            var before = state.CustomerReviews.Where(r => r.Reference == key).ToList();
            state.CustomerReviews.RemoveAll(r => r.Reference == key);
            state.CustomerReviews.Add(new(key, operationId, customerId, customer.CustomerName, actor, DateTimeOffset.UtcNow));
            state.Audit.Add(new(DateTimeOffset.UtcNow, actor, "CustomerConfirmed", null, JsonSerializer.Serialize(before), JsonSerializer.Serialize(state.CustomerReviews.Where(r => r.Reference == key)), "Customer review only; no HCP job or accounting assignment."));
            return Task.FromResult(true);
        }, ct);
    }
    public async Task MatchCustomersAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var actor = HomeDepotHistoryService.Administrator(user);
        await using var db = await factory.CreateDbContextAsync(ct);
        var operations = await db.LocalOperations.AsNoTracking().Where(o => o.IsActive).ToListAsync(ct);
        var sources = new List<(LocalOperation Operation, List<HcpSupplierJob> Jobs)>(); var incomplete = false;
        foreach (var operation in operations)
        {
            try { sources.Add((operation, await hcp.GetSupplierJobsAsync(operation.Slug, ct))); }
            catch (OperationCanceledException) { throw; }
            catch { incomplete = true; }
        }
        await store.ChangeAsync(state => {
            var matches = state.Purchases.SelectMany(p => p.ReportingLines).Select(Reference).Distinct().ToDictionary(r => r,
                r => sources.SelectMany(s => HomeDepotJobMatcher.Find([r], s.Jobs, s.Operation.Id, s.Operation.EffectiveDisplayName)).ToList());
            foreach (var purchase in state.Purchases)
            {
                purchase.Review.Candidates = purchase.ReportingLines.Select(Reference).Distinct().SelectMany(r => matches[r]).DistinctBy(c => (c.OperationId, c.JobId, c.MatchedReference)).ToList();
                purchase.Review.LookupAt = DateTimeOffset.UtcNow;
                purchase.Review.LookupStatus = incomplete ? "IncompleteOperationCoverage" : "CandidatesRequireReview";
                purchase.Version++;
            }
            state.Audit.Add(new(DateTimeOffset.UtcNow, actor, "CustomerSearch", null, "", "", incomplete ? "Some operation searches unavailable; results incomplete." : "Read-only customer proposals refreshed; no assignments."));
            return Task.FromResult(true);
        }, ct);
    }
}
