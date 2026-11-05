using System.Security.Cryptography;
using System.Text;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Options;
using CharleyCompany.Dashboard.Web.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Endpoints;

public static class HomeDepotHistoryEndpoints
{
    private static bool Key(string? expected, string supplied) => !string.IsNullOrWhiteSpace(expected)
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
    private static bool Secure(HttpContext context, HomeDepotHistoryOptions settings, IWebHostEnvironment env) =>
        SupplierInvoiceTransport.IsSecure(context.Request.IsHttps, env.IsDevelopment(), context.Connection.RemoteIpAddress,
            context.Request.Headers["X-Forwarded-Proto"].ToString(), settings.TrustedProxyAddresses);

    public static IEndpointRouteBuilder MapHomeDepotHistoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/home-depot-history/intake", async (HttpContext context, IOptions<HomeDepotHistoryOptions> options,
            IWebHostEnvironment env, HomeDepotHistoryService service, CancellationToken ct) =>
        {
            var settings = options.Value;
            if (!settings.Enabled) return Results.StatusCode(503);
            if (!Secure(context, settings, env) || !Key(settings.IngestionKey, context.Request.Headers["X-CCV-HomeDepot-Key"].ToString())) return Results.Unauthorized();
            if (!context.Request.HasFormContentType || context.Request.ContentLength is null or > HomeDepotHistoryStore.MaxBytes + 65536) return Results.BadRequest(new { error = "A bounded multipart original is required." });
            try
            {
                var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = HomeDepotHistoryStore.MaxBytes + 65536;
                var form = await new FormFeature(context.Request, new FormOptions { MultipartBodyLengthLimit = HomeDepotHistoryStore.MaxBytes, ValueLengthLimit = 4096, ValueCountLimit = 4 }).ReadFormAsync(ct);
                var file = form.Files.GetFile("document");
                if (file is null || form.Files.Count != 1 || file.Length > HomeDepotHistoryStore.MaxBytes) return Results.BadRequest(new { error = "Supply one original." });
                using var memory = new MemoryStream(); await file.CopyToAsync(memory, ct);
                var kind = form["kind"].ToString();
                return Results.Ok(kind == "Csv"
                    ? await service.ImportCsvAsync(memory.ToArray(), "collector:home-depot", ct)
                    : await service.AttachPdfAsync(form["purchaseId"].ToString(), memory.ToArray(), kind, "collector:home-depot", ct));
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (InvalidDataException) { return Results.BadRequest(new { error = "Invalid source upload." }); }
            catch (InvalidOperationException) { return Results.Problem("Home Depot intake is not configured.", statusCode: 503); }
        }).AllowAnonymous().DisableAntiforgery().RequireRateLimiting("supplier-invoices")
            .WithMetadata(new RequestSizeLimitAttribute(HomeDepotHistoryStore.MaxBytes + 65536));

        foreach (var route in new[] { "/api/home-depot-history/materials", "/api/home-depot-history/ap" })
        endpoints.MapGet(route, async (HttpContext context, IOptions<HomeDepotHistoryOptions> options,
            IWebHostEnvironment env, HomeDepotHistoryService service, long? afterRevision, bool? accountingOnly, CancellationToken ct) =>
        {
            if (!options.Value.Enabled || !Secure(context, options.Value, env)) return Results.Unauthorized();
            var admin = context.User.IsInRole(ApplicationRoles.Administrator);
            if (!admin && !Key(options.Value.AccountingReadKey, context.Request.Headers["X-CCV-HomeDepot-Read-Key"].ToString())) return Results.Unauthorized();
            context.Response.Headers.CacheControl = "no-store, private";
            if (route.EndsWith("/ap", StringComparison.Ordinal)) return Results.Json(new { error = "Home Depot purchases never create accounts payable. Use the materials feed; credit-card reconciliation is deferred." }, statusCode: 410);
            return Results.Ok(await service.FeedAsync(afterRevision ?? 0, false, admin ? HomeDepotHistoryService.Administrator(context.User) : "reader:materials", ct));
        }).AllowAnonymous().RequireRateLimiting("supplier-invoices");

        endpoints.MapGet("/api/home-depot-history/documents/{id}", async (string id, HttpContext context,
            IOptions<HomeDepotHistoryOptions> options, IWebHostEnvironment env, HomeDepotHistoryStore store, CancellationToken ct) =>
        {
            if (!options.Value.Enabled || !Secure(context, options.Value, env)) return Results.Unauthorized();
            var admin = context.User.IsInRole(ApplicationRoles.Administrator);
            if (!admin && !Key(options.Value.AccountingReadKey, context.Request.Headers["X-CCV-HomeDepot-Read-Key"].ToString())) return Results.Unauthorized();
            context.Response.Headers.CacheControl = "no-store, private";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            try
            {
                var document = await store.DocumentAsync(id, admin ? HomeDepotHistoryService.Administrator(context.User) : "reader:accounting-review", ct);
                return Results.File(document.Bytes, document.Type, "original" + (document.Type == "text/csv" ? ".csv" : ".pdf"));
            }
            catch (ArgumentException) { return Results.NotFound(); }
        }).AllowAnonymous().RequireRateLimiting("supplier-invoices");
        return endpoints;
    }
}
