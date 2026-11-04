using System.Text.RegularExpressions;
using System.Text.Json;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Models;
using Microsoft.EntityFrameworkCore;
namespace CharleyCompany.Dashboard.Web.Services;

public sealed class SupplyLibraryExceptionService(IDbContextFactory<ApplicationDbContext> factory)
{
    private static string Key(string? value) => string.Join(" ", Regex.Replace(value ?? "", @"[^\p{L}\p{N}]+", " ").ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static string Unit(string? value) => Key(value) switch { "EA" => "EACH", "BX" => "BOX", "PK" => "PACK", var other => other };
    public async Task<List<SupplyLibraryException>> ReadAsync(int vendorId, DateOnly? from, DateOnly? through, bool includeLibrary, CancellationToken ct)
    {
        if (from > through) throw new ArgumentException("The start date must precede the end date.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var cq = db.VendorProducts.AsNoTracking().Include(x => x.SupplyVendor).Include(x => x.Product).Include(x => x.Prices).Include(x => x.PackagingVersions).AsSplitQuery().AsQueryable();
        if (vendorId > 0) cq = cq.Where(x => x.SupplyVendorId == vendorId);
        var catalog = await cq.ToListAsync(ct);
        var indexed = catalog.ToDictionary(x => x.Id);
        var output = new List<SupplyLibraryException>();
        SupplyLibraryException New(VendorProduct item, string key, string source, DateOnly? date) => new() { Key=key, Source=source, VendorId=item.SupplyVendorId, CatalogVendorId=item.SupplyVendorId, Vendor=item.SupplyVendor.Name, Sku=item.VendorSku, Description=item.VendorDescription ?? item.Product.Name, Date=date };
        void Values(SupplyLibraryException row, VendorProduct item, DateOnly date)
        {
            var pack=VendorPackagingService.At(item,date);
            row.CatalogUnit=pack?.PurchaseUnit ?? item.PurchaseUnit;
            row.CatalogPackage=pack?.PackageQuantity ?? item.PackageQuantity;
            row.CatalogPackagingConfirmed=pack?.IsConfirmed == true;
            row.CatalogPrice=item.Prices.Where(x=>x.EffectiveDate<=date && (x.ExpirationDate==null || x.ExpirationDate>=date)).OrderByDescending(x=>x.EffectiveDate).Select(x=>(decimal?)x.UnitPrice).FirstOrDefault();
        }
        void Flag(SupplyLibraryException row,string type,string detail) { if(!row.Categories.Contains(type)) row.Categories.Add(type); row.Details.Add(detail); }
        var lq=db.InvoiceLines.AsNoTracking().Include(x=>x.PayableInvoice).ThenInclude(x=>x.LocalOperation).Include(x=>x.PayableInvoice).ThenInclude(x=>x.SupplyVendor).AsQueryable();
        if(vendorId>0) lq=lq.Where(x=>x.PayableInvoice.SupplyVendorId==vendorId);
        if(from is not null) lq=lq.Where(x=>x.PayableInvoice.InvoiceDate>=from);
        if(through is not null) lq=lq.Where(x=>x.PayableInvoice.InvoiceDate<=through);
        foreach(var line in await lq.ToListAsync(ct))
        {
            if(!indexed.TryGetValue(line.VendorProductId,out var item))
            {
                var missing=new SupplyLibraryException { Key="invoice-line:"+line.Id,Source="Invoice",VendorId=line.PayableInvoice.SupplyVendorId,
                    Vendor=line.PayableInvoice.SupplyVendor.Name,Sku=line.SkuSnapshot ?? "Unknown",InvoiceId=line.PayableInvoiceId,Reference=line.PayableInvoice.InvoiceNumber,
                    Date=line.PayableInvoice.InvoiceDate,Quantity=line.Quantity,UnitPrice=line.UnitPrice,LineTotal=line.LineTotal };
                Flag(missing,"SKU mapping","Invoice line's catalog mapping is missing from this vendor's library.");output.Add(missing);continue;
            }
            var inv=line.PayableInvoice; var row=New(item,"invoice-line:"+line.Id,"Invoice",inv.InvoiceDate);row.VendorId=inv.SupplyVendorId;row.Vendor=inv.SupplyVendor.Name;
            row.InvoiceId=inv.Id;row.Reference=inv.InvoiceNumber;row.Location=inv.LocalOperation.EffectiveDisplayName;row.Job=inv.JobNumber;
            row.Sku=line.SkuSnapshot ?? item.VendorSku;row.Description=line.DescriptionSnapshot ?? item.VendorDescription ?? item.Product.Name;
            row.InvoiceUnit=line.PurchaseUnitSnapshot;row.InvoicePackage=line.PackageQuantitySnapshot;row.InvoicePackagingConfirmed=line.PackagingConfirmed;
            row.Quantity=line.Quantity;row.UnitPrice=line.UnitPrice;row.LineTotal=line.LineTotal;Values(row,item,inv.InvoiceDate);
            if(inv.SupplyVendorId!=item.SupplyVendorId) Flag(row,"SKU mapping","Invoice supplier differs from the linked catalog supplier.");
            if(!item.IsActive) Flag(row,"Inactive SKU","Invoice references an inactive catalog item.");
            if(Key(row.Sku)!=Key(item.VendorSku)) Flag(row,"SKU mapping","Invoice SKU differs from linked catalog SKU: "+item.VendorSku);
            if(row.InvoiceUnit is null || row.InvoicePackage is null || !row.InvoicePackagingConfirmed) Flag(row,"Unknown invoice packaging","Historical invoice packaging was not established; today's catalog is not substituted.");
            if(!row.CatalogPackagingConfirmed) Flag(row,"Unverified catalog packaging","Effective catalog packaging is unverified. This alone does not prove a supplier change.");
            if(row.InvoiceUnit is not null && Unit(row.InvoiceUnit)!=Unit(row.CatalogUnit)) Flag(row,"Purchase unit difference","Invoice and dated catalog units differ.");
            if(row.InvoicePackagingConfirmed && row.InvoicePackage is not null && row.InvoicePackage!=row.CatalogPackage) Flag(row,"Package quantity difference",row.CatalogPackagingConfirmed ? "Confirmed counts differ." : "Invoice count differs from an unverified legacy catalog count.");
            row.ComparablePrices=row.InvoicePackagingConfirmed && row.CatalogPackagingConfirmed && row.InvoicePackage==row.CatalogPackage && Unit(row.InvoiceUnit)==Unit(row.CatalogUnit);
            if(row.CatalogPrice is null) Flag(row,"Missing dated price","No catalog price covers the invoice date.");
            else if(row.CatalogPrice!=line.UnitPrice) Flag(row,"Price difference",row.ComparablePrices ? "Invoice price differs from the effective catalog price." : "Prices differ; establish matching unit/package basis before comparing cost.");
            if(line.PriceReviewStatus=="Pending" && (!row.CatalogPackagingConfirmed || row.CatalogPrice!=line.UnitPrice || Unit(row.InvoiceUnit)!=Unit(row.CatalogUnit) || row.InvoicePackagingConfirmed && row.InvoicePackage!=row.CatalogPackage)) Flag(row,"Price review pending","Catalog price decision was deferred at posting.");
            if(line.Quantity<=0 || line.UnitPrice<0 || line.LineTotal<0 || Math.Abs(decimal.Round(line.Quantity*line.UnitPrice,2,MidpointRounding.AwayFromZero)-line.LineTotal)>.01m) Flag(row,"Quantity / extension","Quantity times unit price does not reconcile to line total, or values require review. Original values retained.");
            if((Unit(row.InvoiceUnit) is "EACH" or "BOX" or "PACK") && decimal.Truncate(line.Quantity)!=line.Quantity) Flag(row,"Fractional count quantity","Count-based unit has a fractional quantity; verify without automatically rounding.");
            if(line.DescriptionSnapshot is not null && Key(line.DescriptionSnapshot)!=Key(item.VendorDescription ?? item.Product.Name)) Flag(row,"Description difference","Description differs from the current catalog; spelling differences may be harmless.");
            if(row.Categories.Count>0)
            {
                if(row.Categories.All(x=>x is "Unknown invoice packaging" or "Description difference")) row.NeedsReview=false;
                output.Add(row);
            }
        }
        var pq=db.PriceImportRows.AsNoTracking().Include(x=>x.PriceImportDocument).ThenInclude(x=>x.SupplyVendor).Where(x=>x.ReviewStatus=="Pending");
        if(vendorId>0) pq=pq.Where(x=>x.PriceImportDocument.SupplyVendorId==vendorId);
        if(from is not null) pq=pq.Where(x=>x.EffectiveDate>=from);
        if(through is not null) pq=pq.Where(x=>x.EffectiveDate<=through);
        var pending=await pq.ToListAsync(ct);
        var postedDocumentIds=(await db.SupplierInvoiceImports.AsNoTracking().Where(x=>x.Status=="Posted").Select(x=>x.PriceImportDocumentId).ToListAsync(ct)).ToHashSet();
        var competing=pending.GroupBy(x=>(x.PriceImportDocument.SupplyVendorId,Key(x.VendorSku),x.EffectiveDate)).Where(g=>g.Select(x=>x.ProposedUnitPrice).Distinct().Count()>1).Select(g=>g.Key).ToHashSet();
        foreach(var proposal in pending.Where(x=>!postedDocumentIds.Contains(x.PriceImportDocumentId)))
        {
            var item=proposal.VendorProductId is int id ? indexed.GetValueOrDefault(id) : null;
            var row=item is null ? new SupplyLibraryException {Key="price-import:"+proposal.Id,Source="Price import",VendorId=proposal.PriceImportDocument.SupplyVendorId,Vendor=proposal.PriceImportDocument.SupplyVendor.Name,Sku=proposal.VendorSku,Date=proposal.EffectiveDate} : New(item,"price-import:"+proposal.Id,"Price import",proposal.EffectiveDate);
            row.Reference=proposal.PriceImportDocument.OriginalFileName;row.Description=proposal.Description;row.UnitPrice=proposal.ProposedUnitPrice;
            if(item is null) Flag(row,"Unmatched SKU","Price proposal has no linked catalog item.");
            else {Values(row,item,proposal.EffectiveDate);if(!item.IsActive) Flag(row,"Inactive SKU","Proposal references an inactive item.");}
            Flag(row,"Price review pending","Staged price awaits a decision.");
            if(competing.Contains((row.VendorId,Key(row.Sku),proposal.EffectiveDate))) Flag(row,"Competing price proposals","Different prices proposed for the same supplier SKU and date.");
            output.Add(row);
        }
        var iq=db.SupplierInvoiceImports.AsNoTracking().Include(x=>x.PayableInvoice).ThenInclude(x=>x!.SupplyVendor)
            .Include(x=>x.PayableInvoice).ThenInclude(x=>x!.LocalOperation).Where(x=>x.Status=="Posted" && x.PayableInvoice!=null);
        if(vendorId>0) iq=iq.Where(x=>x.PayableInvoice!.SupplyVendorId==vendorId);
        if(from is not null) iq=iq.Where(x=>x.PayableInvoice!.InvoiceDate>=from);
        if(through is not null) iq=iq.Where(x=>x.PayableInvoice!.InvoiceDate<=through);
        foreach(var import in await iq.ToListAsync(ct))
        {
            try
            {
                var payload=JsonSerializer.Deserialize<SupplierInvoicePayload>(import.PayloadJson,SupplierInvoicePayload.JsonOptions);
                var notes=(payload?.PostingConflicts ?? []).Where(x=>new[]{"sku","catalog","packag","unit","price","quantity","line","reconcile"}.Any(term=>x.Contains(term,StringComparison.OrdinalIgnoreCase))).ToList();
                if(notes.Count==0) continue;
                var invoice=import.PayableInvoice!;
                var row=new SupplyLibraryException {Key="posting-notes:"+import.Id,Source="Posting notes",VendorId=invoice.SupplyVendorId,
                    Vendor=invoice.SupplyVendor.Name,Reference=invoice.InvoiceNumber,InvoiceId=invoice.Id,Date=invoice.InvoiceDate,
                    Location=invoice.LocalOperation.EffectiveDisplayName,Job=invoice.JobNumber,NeedsReview=false};
                row.Categories.Add("Recorded posting conflict");row.Details.Add("Saved at posting, not automatically reclassified as a current conflict. Compare the current invoice cases before resolving.");
                row.Details.AddRange(notes);output.Add(row);
            }
            catch(JsonException)
            {
                var invoice=import.PayableInvoice!;
                var row=new SupplyLibraryException {Key="posting-notes:"+import.Id,Source="Posting notes",VendorId=invoice.SupplyVendorId,Vendor=invoice.SupplyVendor.Name,Reference=invoice.InvoiceNumber,InvoiceId=invoice.Id,Date=invoice.InvoiceDate};
                Flag(row,"Unreadable posting evidence","Saved import data cannot be read; original invoice records have been retained.");output.Add(row);
            }
        }
        if(includeLibrary)
        {
            var duplicateSkus=catalog.GroupBy(x=>(x.SupplyVendorId,Key(x.VendorSku))).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet();
            var today=DateOnly.FromDateTime(DateTime.Today);
            foreach(var item in catalog)
            {
                var row=New(item,"catalog:"+item.Id,"Library",today);row.Reference="Current catalog";Values(row,item,today);
                if(string.IsNullOrWhiteSpace(item.PurchaseUnit)||item.PackageQuantity<=0) Flag(row,"Invalid catalog packaging","Current unit or quantity is invalid.");
                if(string.IsNullOrWhiteSpace(item.VendorSku)) Flag(row,"SKU mapping","Catalog SKU is blank.");
                if(duplicateSkus.Contains((item.SupplyVendorId,Key(item.VendorSku)))) Flag(row,"Ambiguous SKU","Multiple items have the same normalized SKU in this vendor.");
                if(row.Categories.Count>0) output.Add(row);
                else if(item.IsActive && (!row.CatalogPackagingConfirmed || row.CatalogPrice is null))
                {
                    row.NeedsReview=false;
                    if(!row.CatalogPackagingConfirmed) Flag(row,"Unverified catalog packaging","Current recorded packaging lacks supplier confirmation.");
                    if(row.CatalogPrice is null) Flag(row,"Missing current price","No catalog price covers today.");
                    output.Add(row);
                }
                var versions=item.PackagingVersions.Where(x=>x.IsConfirmed).OrderBy(x=>x.EffectiveDate).ToList();
                for(var i=1;i<versions.Count;i++)
                {
                    var before=versions[i-1];var after=versions[i];
                    if(before.PurchaseUnit==after.PurchaseUnit && before.PackageQuantity==after.PackageQuantity) continue;
                    if(from is not null && after.EffectiveDate<from || through is not null && after.EffectiveDate>through) continue;
                    var change=New(item,"packaging-version:"+after.Id,"Packaging history",after.EffectiveDate);change.NeedsReview=false;
                    change.InvoiceUnit=before.PurchaseUnit;change.InvoicePackage=before.PackageQuantity;change.InvoicePackagingConfirmed=true;
                    change.CatalogUnit=after.PurchaseUnit;change.CatalogPackage=after.PackageQuantity;change.CatalogPackagingConfirmed=true;
                    Flag(change,"Confirmed packaging change","Previous and new confirmed packaging. Evidence: "+after.Evidence);output.Add(change);
                }
            }
        }
        return output;
    }
}
