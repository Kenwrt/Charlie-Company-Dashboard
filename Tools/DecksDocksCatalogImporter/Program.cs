using System.Security.Cryptography;
using CharleyCompany.Dashboard.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var sourceDirectory = args.Length == 1
    ? Path.GetFullPath(args[0])
    : throw new ArgumentException("Usage: DecksDocksCatalogImporter <invoice-pdf-directory>");

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets("aspnet-CharleyCompany_Dashboard_Web-91c21216-f639-4916-8ebd-97068201d658")
    .AddEnvironmentVariables()
    .Build();
var connectionString = configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
    .Options;

await using var db = new ApplicationDbContext(options);
if (!await db.Database.CanConnectAsync())
{
    throw new InvalidOperationException("The configured PostgreSQL database is not reachable.");
}

var vendor = await db.SupplyVendors
    .Include(x => x.VendorProducts).ThenInclude(x => x.Product)
    .Include(x => x.VendorProducts).ThenInclude(x => x.Prices)
    .SingleOrDefaultAsync(x => x.Name.ToLower().Contains("decks") && x.Name.ToLower().Contains("docks"));
if (vendor is null)
{
    vendor = new SupplyVendor
    {
        Name = "Decks & Docks",
        LegalName = "Decks & Docks Lumber Company, Inc.",
        Phone = "615-835-3769",
        AddressLine1 = "344 Wilhagan Rd.",
        City = "Nashville",
        StateOrProvince = "TN",
        PostalCode = "37217"
    };
    db.SupplyVendors.Add(vendor);
    await db.SaveChangesAsync();
}

var storageRoot = configuration["CatalogImports:StoragePath"];
if (string.IsNullOrWhiteSpace(storageRoot))
{
    storageRoot = Path.Combine(Directory.GetCurrentDirectory(), "CatalogImports");
}
var vendorStorage = Path.Combine(storageRoot, "invoice-derived", "decks-and-docks");
Directory.CreateDirectory(vendorStorage);

var addedDocuments = 0;
var addedProducts = 0;
var addedPrices = 0;
var addedRows = 0;

foreach (var invoice in Invoices.All)
{
    var sourcePath = Path.Combine(sourceDirectory, invoice.FileName);
    if (!File.Exists(sourcePath)) throw new FileNotFoundException($"Invoice PDF not found: {sourcePath}");
    var bytes = await File.ReadAllBytesAsync(sourcePath);
    var hash = Convert.ToHexString(SHA256.HashData(bytes));
    var document = await db.PriceImportDocuments
        .Include(x => x.Rows)
        .SingleOrDefaultAsync(x => x.Sha256 == hash);
    if (document is null)
    {
        var storedPath = Path.Combine(vendorStorage, invoice.FileName);
        if (!File.Exists(storedPath)) await File.WriteAllBytesAsync(storedPath, bytes);
        document = new PriceImportDocument
        {
            SupplyVendorId = vendor.Id,
            OriginalFileName = invoice.FileName,
            StoragePath = storedPath,
            Sha256 = hash,
            ContentType = "application/pdf",
            Status = "Imported - Invoice Derived"
        };
        db.PriceImportDocuments.Add(document);
        addedDocuments++;
    }

    foreach (var line in invoice.Lines)
    {
        var item = vendor.VendorProducts.SingleOrDefault(x =>
            x.VendorSku.Equals(line.Sku, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            var product = new Product
            {
                Name = Truncate(line.Description, 160),
                Category = CategoryFor(line.Description),
                Manufacturer = ManufacturerFor(line.Description),
                ManufacturerPartNumber = line.Sku,
                UnitOfMeasure = line.Unit
            };
            item = new VendorProduct
            {
                SupplyVendorId = vendor.Id,
                Product = product,
                VendorSku = line.Sku,
                VendorDescription = Truncate(line.Description, 300),
                PackageQuantity = 1,
                PurchaseUnit = line.Unit
            };
            vendor.VendorProducts.Add(item);
            addedProducts++;
        }

        var price = item.Prices.SingleOrDefault(x => x.EffectiveDate == invoice.InvoiceDate);
        if (price is null)
        {
            item.Prices.Add(new VendorPrice
            {
                UnitPrice = line.UnitPrice,
                EffectiveDate = invoice.InvoiceDate,
                SourceType = "Invoice Derived",
                SourceReference = invoice.FileName
            });
            addedPrices++;
        }
        else if (price.UnitPrice != line.UnitPrice)
        {
            throw new InvalidOperationException(
                $"Conflicting price for {line.Sku} on {invoice.InvoiceDate}: {price.UnitPrice} vs {line.UnitPrice}.");
        }

        if (document.Rows.All(x => !x.VendorSku.Equals(line.Sku, StringComparison.OrdinalIgnoreCase)))
        {
            document.Rows.Add(new PriceImportRow
            {
                VendorProduct = item,
                VendorSku = line.Sku,
                Description = Truncate(line.Description, 300),
                ProposedUnitPrice = line.UnitPrice,
                EffectiveDate = invoice.InvoiceDate,
                MatchConfidence = 1m,
                ReviewStatus = "Approved"
            });
            addedRows++;
        }
    }
    await db.SaveChangesAsync();
}

foreach (var item in vendor.VendorProducts)
{
    var prices = item.Prices.OrderBy(x => x.EffectiveDate).ToList();
    for (var index = 0; index < prices.Count; index++)
    {
        prices[index].ExpirationDate = index + 1 < prices.Count
            ? prices[index + 1].EffectiveDate.AddDays(-1)
            : null;
    }
}
vendor.UpdatedAt = DateTimeOffset.UtcNow;
await db.SaveChangesAsync();

var documentCount = await db.PriceImportDocuments.CountAsync(x => x.SupplyVendorId == vendor.Id);
var productCount = await db.VendorProducts.CountAsync(x => x.SupplyVendorId == vendor.Id);
var priceCount = await db.VendorPrices.CountAsync(x => x.VendorProduct.SupplyVendorId == vendor.Id);
Console.WriteLine($"VendorId={vendor.Id}");
Console.WriteLine($"Added documents={addedDocuments}, products={addedProducts}, prices={addedPrices}, review rows={addedRows}");
Console.WriteLine($"Decks & Docks totals: documents={documentCount}, products={productCount}, price versions={priceCount}");

static string Truncate(string value, int length) =>
    value.Length <= length ? value : value[..length];

static string? ManufacturerFor(string description) =>
    description.Contains("Trex", StringComparison.OrdinalIgnoreCase) ? "Trex" :
    description.Contains("Simpson", StringComparison.OrdinalIgnoreCase) ? "Simpson Strong-Tie" :
    null;

static string CategoryFor(string description) =>
    description.Contains("Railing", StringComparison.OrdinalIgnoreCase) ||
    description.Contains("Baluster", StringComparison.OrdinalIgnoreCase) ||
    description.Contains("Rail Cap", StringComparison.OrdinalIgnoreCase) ? "Railing" :
    description.Contains("Fascia", StringComparison.OrdinalIgnoreCase) ? "Fascia" :
    description.Contains("Deck", StringComparison.OrdinalIgnoreCase) ? "Decking and Accessories" :
    description.Contains("Hanger", StringComparison.OrdinalIgnoreCase) ||
    description.Contains("Post Base", StringComparison.OrdinalIgnoreCase) ||
    description.Contains("Anchor", StringComparison.OrdinalIgnoreCase) ? "Connectors" :
    description.Contains("Nail", StringComparison.OrdinalIgnoreCase) ||
    description.Contains("Screw", StringComparison.OrdinalIgnoreCase) ||
    description.Contains("Plug", StringComparison.OrdinalIgnoreCase) ? "Fasteners" :
    description.Contains("OSB", StringComparison.OrdinalIgnoreCase) ? "Sheet Goods" :
    "Lumber and Building Materials";

internal sealed record InvoiceCatalogLine(
    string Sku,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    string Unit = "Each");

internal sealed record SourceInvoice(
    string FileName,
    DateOnly InvoiceDate,
    IReadOnlyList<InvoiceCatalogLine> Lines);

internal static class Invoices
{
    public static readonly SourceInvoice[] All =
    [
        new("INV-1308260.pdf", new DateOnly(2026, 5, 18),
        [
            L("zz_$SOKL_0381", "3-1/4\"x48\" Post W/ Plate SATIN STORM", 3, 147.60m),
            L("zz_$SOKL_0385", "3-1/4\" x 51-3/8\" Fascia Mount Level Post Satin Storm", 15, 127.73m),
            L("zz_$SOKL_0386", "3-1/4\" x 53-3/8\" Fascia Mount Stair Post Satin Storm", 5, 127.73m),
            L("zz_$SOKL_0387", "Fascia Mount Kit 3-1/4\" Inside Corner Post Satin Storm", 1, 104.39m),
            L("zz_$SOKL_0388", "Fascia Mount Kit 3-1/4\" Line Post Satin Storm", 15, 104.39m),
            L("zz_$SOKL_0389", "Fascia Mount Kit 3-1/4\" Outside Corner Post Satin Storm", 4, 104.39m),
            L("zz_$SOKL_0390", "Fascia Mount Support Section Assembly Satin Storm", 5, 46.74m),
            L("zz_$SOKL_0391", "42\" x 8' Vertical Cable Railing Level Section (Chesapeake Series) Satin Storm", 5, 687.44m),
            L("zz_$SOKL_0392", "42\" x 6' Vertical Cable Railing Level Section (Chesapeake Series) Satin Storm", 8, 515.59m),
            L("zz_$SOKL_0393", "42\" x 6' Stair Section w/Square Balusters (Chesapeake Series) Satin Storm", 4, 350.96m),
            L("zz_$SOKL_0394", "42\" x 8' Stair Section w/Square Balusters (Chesapeake Series) Satin Storm", 1, 449.25m),
            L("zz_$SOKL_0395", "42\" x 6' Level Section w/Square Balusters (Chesapeake Series) Satin Storm", 2, 350.96m),
            L("zz_$SOKL_0396", "4-Pk Swivel Horizontal Mounting Bracket (Chesapeake Series) Satin Storm", 1, 95.31m),
            L("zz_$SOKL_0397", "17' Rail Cap Chesapeake Series (2 Per Box)", 4, 244.61m)
        ]),
        new("INV-1356308.pdf", new DateOnly(2026, 7, 8),
        [
            L("66N12", "6x6x12 #1 - GC", 6, 43.71m),
            L("66N8", "6x6x8 #1 - GC", 1, 29.85m),
            L("ABA66Z", "Simpson Adjustable/Standoff Post Base 6X6 - Zmax.", 4, 25.50m),
            L("210NN12P", "2x10x12 #2 Prime - GC", 32, 21.40m),
            L("210NN16P", "2x10x16 #2 Prime - GC", 21, 28.32m),
            L("212NN16P", "2x12x16 #2 Prime - GC", 6, 35.52m),
            L("28NN8P", "2x8x8 #2 Prime - GC", 20, 10.91m),
            L("26NN16P", "2x6x16 #2 Prime - GC", 7, 16.02m),
            L("LUS28Z", "Simpson 2 X 8 Shear Face Mount Hanger Zmax", 19, 1.85m),
            L("015HDG", "Gun Nail 22 Deg 3\" Spiral 4M", 1, 134.96m),
            L("716OSB", "7/16\" x 4' x 8' OSB", 12, 11.96m)
        ]),
        new("INV-1367342.pdf", new DateOnly(2026, 7, 20),
        [
            L("TESPSBH636", "Trex Enhance Steel 6'x36\" Panel W/ Square Balusters Horizontal", 10, 139.27m),
            L("TESPSBS636", "Trex Enhance Steel 6'x36\" Panel W/ Square Balusters Stair", 2, 187.25m),
            L("TESPSBH836", "Trex Enhance Steel 8'x36\" Panel W/ Square Balusters Horizontal", 2, 179.80m)
        ]),
        new("INV-1368464.pdf", new DateOnly(2026, 7, 21),
        [
            L("TESFBH", "Trex Enhance Steel Fixed Horizontal Bracket (4 Pack)", 7, 76.51m),
            L("TESPSBH636", "Trex Enhance Steel 6'x36\" Panel W/ Square Balusters Horizontal", 2, 139.27m),
            L("TENDPLGS2BK75", "Trex Enhance Decking Plug Screws 2\" -75ct", 2, 14.70m),
            L("TPSENDCBKIT", "Trex Enhance Plug Counterbore And Drive Tool Kit", 1, 38.45m)
        ]),
        new("INV-1371102.pdf", new DateOnly(2026, 7, 23),
        [
            L("ABA66Z", "Simpson Adjustable/Standoff Post Base 6X6 - Zmax.", 6, 25.50m),
            L("THD50400HMG", "Simpson TITEN HD MG 1/2\"x4\" Heavy-Duty Screw Anchor 20/ct", 6, 4.92m),
            L("66N10", "6x6x10 #1 - GC", 6, 36.51m),
            L("TXE1812PB", "Trex Enhance Basics Fascia 1x8x12 - Pebble Beach", 17, 77.88m),
            L("TXFPSDFPB416", "Trex Hideaway Enhance Deck/Fascia Plugs Pebble Beach (416ct.)", 1, 59.93m),
            L("TPSDFPB80", "Trex Hideaway Enhance Deck/Fascia Plugs Pebble Beach (80ct.)", 1, 32.52m),
            L("TENDPLGS2BK75", "Trex Enhance Decking Plug Screws 2\" -75ct", 6, 14.70m),
            L("TPSENDCBKIT", "Trex Enhance Plug Counterbore And Drive Tool Kit", 1, 38.45m),
            L("TESPSBH636", "Trex Enhance Steel 6'x36\" Panel W/ Square Balusters Horizontal", 10, 139.27m),
            L("TESPSBS836", "Trex Enhance Steel 8'x36\" Panel W/ Square Balusters Stair", 1, 255.89m),
            L("TESBP2253H", "Trex Enhance Steel Blank Post 2\"x2\"x53\" Stair", 3, 80.44m),
            L("TESBP2237H", "Trex Enhance Steel Blank Post 2\"x2\"x37\" Horizontal", 5, 69.44m),
            L("TESSBS", "Trex Enhance Steel Swivel Stair Bracket (2 Pack)", 2, 91.21m),
            L("TESFBH", "Trex Enhance Steel Fixed Horizontal Bracket (4 Pack)", 10, 76.51m)
        ]),
        new("INV-1371096.pdf", new DateOnly(2026, 7, 23),
        [
            L("TXE1812PB", "Trex Enhance Basics Fascia 1x8x12 - Pebble Beach", 1, 77.88m)
        ]),
        new("INV-1372454.pdf", new DateOnly(2026, 7, 24),
        [
            L("TXE1812PB", "Trex Enhance Basics Fascia 1x8x12 - Pebble Beach", 6, 77.88m),
            L("TXE1616PB", "Trex Enhance Basics 1x6x16 - Pebble Beach - Square", 1, 39.36m)
        ])
    ];

    private static InvoiceCatalogLine L(string sku, string description, decimal quantity, decimal unitPrice) =>
        new(sku, description, quantity, unitPrice);
}
