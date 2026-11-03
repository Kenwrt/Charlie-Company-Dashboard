using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Models;
using CharleyCompany.Dashboard.Web.Options;
using CharleyCompany.Dashboard.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Endpoints;

public static class SupplierInvoiceEndpoints
{
    public static IEndpointRouteBuilder MapSupplierInvoiceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/supplier-invoices/decks-docks", async (HttpContext context,
            IOptions<SupplierInvoiceOptions> options, SupplierInvoiceService service, CancellationToken ct) =>
        {
            var settings = options.Value;
            if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.IngestionKey)) return Results.StatusCode(503);
            if (!SecureTransport(context) || !KeyEquals(settings.IngestionKey, context.Request.Headers["X-CCV-Supplier-Key"].ToString())) return Results.Unauthorized();
            if (!context.Request.HasFormContentType || context.Request.ContentLength > SupplierInvoiceService.MaxPdfBytes + SupplierInvoiceService.MaxPayloadBytes + 65536)
                return Results.BadRequest(new { error = "A bounded multipart invoice upload is required." });
            try
            {
                var maxSize = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (maxSize is { IsReadOnly: false }) maxSize.MaxRequestBodySize = SupplierInvoiceService.MaxPdfBytes + SupplierInvoiceService.MaxPayloadBytes + 65536;
                var form = await new FormFeature(context.Request, new FormOptions
                {
                    MultipartBodyLengthLimit = SupplierInvoiceService.MaxPdfBytes,
                    ValueLengthLimit = SupplierInvoiceService.MaxPayloadBytes,
                    ValueCountLimit = 4, MultipartHeadersCountLimit = 8
                }).ReadFormAsync(ct);
                var pdf = form.Files.GetFile("document");
                var metadata = form["invoice"].ToString();
                if (pdf is null || form.Files.Count != 1 || pdf.Length > SupplierInvoiceService.MaxPdfBytes || Encoding.UTF8.GetByteCount(metadata) > SupplierInvoiceService.MaxPayloadBytes)
                    return Results.BadRequest(new { error = "Supply one PDF and its extracted invoice data." });
                var payload = JsonSerializer.Deserialize<SupplierInvoicePayload>(metadata, SupplierInvoicePayload.JsonOptions);
                if (payload is null) return Results.BadRequest(new { error = "Invoice data is required." });
                using var memory = new MemoryStream();
                await pdf.CopyToAsync(memory, ct);
                var result = await service.StageAsync(memory.ToArray(), pdf.FileName, payload, "hermes:decks-docks", ct,
                    refreshExisting: form["refreshExtraction"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase));
                return Results.Ok(result);
            }
            catch (JsonException) { return Results.BadRequest(new { error = "Invoice data is invalid." }); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (InvalidDataException) { return Results.BadRequest(new { error = "The invoice upload is invalid." }); }
        }).AllowAnonymous().DisableAntiforgery().RequireRateLimiting("supplier-invoices")
            .WithMetadata(new RequestSizeLimitAttribute(SupplierInvoiceService.MaxPdfBytes + SupplierInvoiceService.MaxPayloadBytes + 65536));

        // Separate read-only credential: AP consumers cannot submit or approve invoices.
        endpoints.MapGet("/api/supplier-invoices/ap", async (HttpContext context, IOptions<SupplierInvoiceOptions> options,
            IDbContextFactory<ApplicationDbContext> factory, IWebHostEnvironment environment,
            DateTimeOffset? afterChangedAt, int? afterId, bool? accountingOnly, CancellationToken ct) =>
        {
            if (!CanRead(context, options.Value)) return Results.Unauthorized();
            await using var db = await factory.CreateDbContextAsync(ct);
            var since = afterChangedAt ?? DateTimeOffset.UnixEpoch;
            var cursorId = afterId ?? 0;
            var page = await db.SupplierInvoiceImports.AsNoTracking()
                .Where(x => (x.ReviewedAt ?? x.CreatedAt) > since || ((x.ReviewedAt ?? x.CreatedAt) == since && x.Id > cursorId))
                .OrderBy(x => x.ReviewedAt ?? x.CreatedAt).ThenBy(x => x.Id).Take(100).Select(x => new
                {
                    x.Id, x.Status, x.CorrelationId, x.PayableInvoiceId, x.CreatedAt, x.ReviewedAt,
                    ChangedAt = x.ReviewedAt ?? x.CreatedAt,
                    x.PriceImportDocument.SupplyVendorId, x.PriceImportDocument.Sha256, x.PayloadJson,
                    PayingEntityCode = x.PayableInvoice == null ? null : x.PayableInvoice.PayingEntityCode,
                    LocationSlug = x.PayableInvoice == null ? null : x.PayableInvoice.LocalOperation.Slug,
                    PurchasingLocationId = x.PayableInvoice == null ? (int?)null : x.PayableInvoice.LocalOperationId,
                    YourReference = x.PayableInvoice == null ? null : x.PayableInvoice.YourReference,
                    DocumentUrl = "/api/supplier-invoices/" + x.Id + "/document"
                }).ToListAsync(ct);
            context.Response.Headers.CacheControl = "no-store, private";
            db.OperationalEvents.Add(new OperationalEvent
            {
                Environment = environment.EnvironmentName, Machine = System.Environment.MachineName,
                Service = "CharlieCompanyDashboard", Module = "SupplierInvoices", CorrelationId = Guid.NewGuid().ToString("N"),
                Step = "ApRead", Status = "Completed", Message = "Supplier invoice AP feed accessed."
            });
            await db.SaveChangesAsync(ct);
            // The legacy AP feed includes reporting records. An accounting consumer must
            // request accountingOnly=true and still perform its own accounting approval.
            var candidates = page.Select(x =>
            {
                var payload = JsonSerializer.Deserialize<SupplierInvoicePayload>(x.PayloadJson, SupplierInvoicePayload.JsonOptions)!;
                var approvedOwner = payload.PurchaseEntityCode;
                var approvedPolicy = payload.AccountingPolicy;
                var approvedVersion = payload.AccountingPolicyVersion;
                SupplierInvoiceAccounting.Assign(payload, x.LocationSlug, options.Value);
                var eligible = x.Status == "Posted" && x.PayableInvoiceId is not null
                    && approvedVersion == SupplierInvoiceAccounting.Version
                    && approvedOwner == payload.PurchaseEntityCode && approvedPolicy == "AccountingReview"
                    && payload.AccountingPolicy == "AccountingReview";
                return new
                {
                    x.Id, x.Status, x.CorrelationId, x.PayableInvoiceId, x.CreatedAt, x.ReviewedAt, x.ChangedAt,
                    x.SupplyVendorId, x.Sha256, x.PayloadJson, x.PayingEntityCode, x.PurchasingLocationId,
                    x.YourReference, x.DocumentUrl,
                    PurchaseEntityCode = payload.PurchaseEntityCode,
                    AccountingPolicy = payload.AccountingPolicy,
                    AccountingHandoffStatus = eligible ? "ReadyForAccountingReview" : "NotEligible",
                    EligibleForAccountingReview = eligible,
                    PostingKey = "supplier-invoice:" + x.Id,
                    AutomaticExternalPosting = false
                };
            }).Where(x => accountingOnly != true || x.EligibleForAccountingReview).ToList();
            return Results.Ok(new { items = candidates,
                accountingReviewRequired = true,
                // Advance over the source page even when all entries were excluded.

                nextAfterId = page.Count > 0 ? page[^1].Id : cursorId,
                nextAfterChangedAt = page.Count > 0 ? page[^1].ChangedAt : since });
        }).AllowAnonymous().RequireRateLimiting("supplier-invoices");

        endpoints.MapGet("/api/supplier-invoices/{id:int}/document", async (HttpContext context, int id,
            IOptions<SupplierInvoiceOptions> options, IDbContextFactory<ApplicationDbContext> factory,
            IWebHostEnvironment environment, IConfiguration configuration, CancellationToken ct) =>
        {
            if (!CanRead(context, options.Value)) return Results.Unauthorized();
            await using var db = await factory.CreateDbContextAsync(ct);
            var import = await db.SupplierInvoiceImports.AsNoTracking().Include(x => x.PriceImportDocument).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (import is null) return Results.NotFound();
            var document = import.PriceImportDocument;
            var path = Path.GetFullPath(document.StoragePath);
            var catalogRoot = configuration["CatalogImports:StoragePath"] ?? Path.Combine(environment.ContentRootPath, "CatalogImports");
            if (!Within(path, options.Value.StoragePath) && !Within(path, catalogRoot)) return Results.NotFound();
            if (!File.Exists(path)) return Results.NotFound();
            context.Response.Headers.CacheControl = "no-store, private";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            db.OperationalEvents.Add(new OperationalEvent
            {
                Environment = environment.EnvironmentName, Machine = System.Environment.MachineName,
                Service = "CharlieCompanyDashboard", Module = "SupplierInvoices", CorrelationId = import.CorrelationId,
                Step = "DocumentRead", Status = "Completed", Message = "Supplier invoice original document accessed."
            });
            await db.SaveChangesAsync(ct);
            return Results.File(File.OpenRead(path), "application/pdf", document.OriginalFileName);
        }).AllowAnonymous().RequireRateLimiting("supplier-invoices");
        return endpoints;
    }

    private static bool CanRead(HttpContext context, SupplierInvoiceOptions options) =>
        context.User.IsInRole(ApplicationRoles.Administrator) || (options.Enabled && SecureTransport(context)
            && KeyEquals(options.ApReadKey, context.Request.Headers["X-CCV-AP-Key"].ToString()));

    private static bool SecureTransport(HttpContext context)
    {
        var peer = context.Connection.RemoteIpAddress;
        var settings = context.RequestServices.GetRequiredService<IOptions<SupplierInvoiceOptions>>().Value;
        var secure = SupplierInvoiceTransport.IsSecure(context.Request.IsHttps,
            context.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment(), peer,
            context.Request.Headers["X-Forwarded-Proto"].ToString(), settings.TrustedProxyAddresses);
        if (!secure)
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SupplierInvoices")
                .LogWarning("SupplierInvoiceTransportRejected from peer {PeerAddress}", peer);
        return secure;
    }

    private static bool KeyEquals(string? expected, string supplied) => !string.IsNullOrWhiteSpace(expected) && !string.IsNullOrWhiteSpace(supplied)
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));

    private static bool Within(string path, string root) => path.StartsWith(
        Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
