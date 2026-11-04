using CharleyCompany.Dashboard.Web.Data;
using Microsoft.EntityFrameworkCore;
namespace CharleyCompany.Dashboard.Web.Services;

public sealed class VendorPackagingService(IDbContextFactory<ApplicationDbContext> factory, IWebHostEnvironment environment)
{
    public static VendorPackagingVersion? At(VendorProduct item, DateOnly date) => item.PackagingVersions
        .Where(x => x.EffectiveDate <= date).OrderByDescending(x => x.EffectiveDate).FirstOrDefault();

    public async Task ConfirmAsync(int id, DateOnly date, string unit, decimal quantity, string evidence, string actor, CancellationToken ct)
    {
        unit = unit.Trim(); evidence = evidence.Trim();
        if (unit.Length is < 1 or > 40 || quantity <= 0 || decimal.Round(quantity, 4) != quantity || evidence.Length is < 1 or > 500
            || date <= new DateOnly(1900, 1, 1) || date > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ArgumentException("Provide a purchase unit, positive package quantity (up to four decimals), supplier evidence and a past or current effective date after 1900-01-01.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var vendor = await db.VendorProducts.Where(x => x.Id == id).Select(x => x.SupplyVendorId).SingleAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({720000000000L + vendor})", ct);
        var item = await db.VendorProducts.Include(x => x.PackagingVersions).Include(x => x.Prices).SingleAsync(x => x.Id == id, ct);
        if (!item.IsActive) throw new ArgumentException("Reactivate this catalog item before confirming its packaging.");
        if (item.PackagingVersions.Any(x => x.EffectiveDate >= date))
            throw new ArgumentException("Packaging history is append-only. Choose a date after the latest recorded version; existing versions cannot be overwritten.");
        var changes = !item.PurchaseUnit.Equals(unit, StringComparison.OrdinalIgnoreCase) || item.PackageQuantity != quantity;
        if (changes && item.Prices.Any(x => x.EffectiveDate >= date))
            throw new ArgumentException("Prices already exist on or after this change date. Resolve those price dates before changing packaging; a price cannot silently change its package basis.");
        if (changes)
            foreach (var price in item.Prices.Where(x => x.ExpirationDate == null || x.ExpirationDate >= date)) price.ExpirationDate = date.AddDays(-1);
        if (item.PackagingVersions.Count == 0)
            db.VendorPackagingVersions.Add(new VendorPackagingVersion { VendorProductId = id, EffectiveDate = new DateOnly(1900,1,1),
                PurchaseUnit = item.PurchaseUnit, PackageQuantity = item.PackageQuantity, IsConfirmed = false, Evidence = "Prior catalog values; unverified" });
        db.VendorPackagingVersions.Add(new VendorPackagingVersion { VendorProductId = id, EffectiveDate = date,
            PurchaseUnit = unit, PackageQuantity = quantity, IsConfirmed = true, Evidence = evidence, ConfirmedBy = actor });
        item.PurchaseUnit = unit; item.PackageQuantity = quantity;
        db.OperationalEvents.Add(new OperationalEvent { Environment = environment.EnvironmentName,
            Machine = System.Environment.MachineName, Service = "CharlieCompanyDashboard", Module = "CatalogPackaging",
            CorrelationId = Guid.NewGuid().ToString("N"), Step = "PackagingConfirmed", Status = "Completed",
            Message = "Supplier packaging confirmed with effective date.",
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(new { actor, vendorProductId = id, effectiveDate = date, unit, quantity, changes }) });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
