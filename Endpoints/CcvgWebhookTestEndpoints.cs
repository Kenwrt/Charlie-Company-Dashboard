using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Options;
using CharleyCompany.Dashboard.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Endpoints;

public static class CcvgWebhookTestEndpoints
{
    public static IEndpointRouteBuilder MapCcvgWebhookTestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/webhooks/housecallpro/ccvg", ReceiveAsync)
            .AllowAnonymous().RequireRateLimiting("ccvg-webhook-test");
        endpoints.MapGet("/api/admin/ccvg/webhook-receipts", async (int? page, string? eventType, int? operationId,
            DateTimeOffset? fromUtc, CcvgWebhookTestStore store, HttpContext context, ILoggerFactory logs, CancellationToken ct) =>
        {
            var rows = (await store.ReadAsync(ct)).Where(x => (eventType is null || x.EventType == eventType)
                && (operationId is null || x.OperationId == operationId) && (fromUtc is null || x.ReceivedAtUtc >= fromUtc));
            var number = Math.Max(1, page ?? 1);
            logs.CreateLogger("CcvgWebhookTest").LogInformation("CCVG receipt metadata viewed by {ActorId} at {UtcTime}",
                context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, DateTimeOffset.UtcNow);
            return Results.Ok(new { Total = rows.Count(), Page = number, Items = rows.Skip((int)Math.Min((long)(number - 1) * 50, int.MaxValue)).Take(50).ToArray() });
        }).RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Administrator));
        return endpoints;
    }

    private static async Task<IResult> ReceiveAsync(HttpRequest request, IOptions<CcvgWebhookTestOptions> configured,
        IDbContextFactory<ApplicationDbContext> databases, CcvgWebhookTestStore store,
        ILoggerFactory logs, CancellationToken ct)
    {
        var settings = configured.Value;
        if (!settings.Enabled) return Results.NotFound();
        var maximum = Math.Clamp(settings.MaxBodyBytes, 1024, 1048576);
        if (request.ContentLength > maximum) return Results.StatusCode(413);
        if (request.ContentType is null || !request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            return Results.StatusCode(415);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var logger = logs.CreateLogger("CcvgWebhookTest");
        try
        {
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await request.Body.ReadAsync(buffer, deadline.Token)) > 0)
            {
                if (body.Length + read > maximum) return Results.StatusCode(413);
                body.Write(buffer, 0, read);
            }
            var bytes = body.ToArray();
            // Parse once, but authenticate the original bytes, never reserialized JSON.
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Results.BadRequest();
            var properties = root.EnumerateObject().ToArray();
            if (properties.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length) return Results.BadRequest();
            var timestamp = request.Headers["Api-Timestamp"];
            var signature = request.Headers["Api-Signature"];
            if (settings.AllowRegistrationChallenge && timestamp.Count == 0 && signature.Count == 0
                && properties.Length == 1 && root.TryGetProperty("foo", out var foo)
                && foo.ValueKind == JsonValueKind.String && foo.GetString() == "bar")
            {
                logger.LogInformation("CCVG registration challenge received at {UtcTime} with trace {TraceId}", DateTimeOffset.UtcNow, request.HttpContext.TraceIdentifier);
                return Results.Ok();
            }
            if (string.IsNullOrWhiteSpace(settings.SigningSecret)) return Results.StatusCode(503);
            if (timestamp.Count != 1 || signature.Count != 1 || timestamp[0] is not { } stamp
                || !long.TryParse(stamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                || Math.Abs((decimal)DateTimeOffset.UtcNow.ToUnixTimeSeconds() - seconds) > Math.Clamp(settings.TimestampToleranceSeconds, 30, 900))
                return Results.Unauthorized();
            byte[] supplied;
            try
            {
                var value = signature[0]!;
                supplied = value.Length == 64 ? Convert.FromHexString(value) : Convert.FromBase64String(value);
            }
            catch (FormatException) { return Results.Unauthorized(); }
            var prefix = Encoding.UTF8.GetBytes(stamp + ".");
            using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(settings.SigningSecret));
            hmac.AppendData(prefix);
            hmac.AppendData(bytes);
            if (!CryptographicOperations.FixedTimeEquals(hmac.GetHashAndReset(), supplied)) return Results.Unauthorized();
            if (!root.TryGetProperty("event", out var eventNode) || eventNode.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("company_id", out var companyNode) || companyNode.ValueKind != JsonValueKind.String)
                return Results.BadRequest();
            var eventType = eventNode.GetString()!;
            var companyId = companyNode.GetString()!;
            if (!Regex.IsMatch(eventType, @"\A[a-z][a-z0-9_.]{0,119}\z") || string.IsNullOrWhiteSpace(companyId)
                || companyId.Length > 160 || companyId.Any(char.IsControl)) return Results.BadRequest();
            await using var db = await databases.CreateDbContextAsync(deadline.Token);
            var operationId = await db.LocalOperations.AsNoTracking().Where(x => x.HousecallProLocationId == companyId)
                .Select(x => (int?)x.Id).SingleOrDefaultAsync(deadline.Token);
            var knownFields = new HashSet<string>(["event", "company_id", "event_occurred_at", "job", "estimate", "invoice", "customer", "lead", "appointment", "application"], StringComparer.Ordinal);
            var receipt = new CcvgWebhookReceipt(Convert.ToHexString(SHA256.HashData(bytes)), DateTimeOffset.UtcNow,
                eventType, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(companyId))), operationId, bytes.Length,
                properties.Select(x => x.Name).Where(knownFields.Contains).ToArray());
            var added = await store.AddAsync(receipt, deadline.Token);
            logger.LogInformation("CCVG test webhook {Disposition} for operation {OperationId} at {UtcTime}, trace {TraceId}",
                added ? "recorded" : "duplicate", operationId, DateTimeOffset.UtcNow, request.HttpContext.TraceIdentifier);
            // No legacy handler, dispatcher, notification service or HCP sync is called.
            return Results.Accepted();
        }
        catch (JsonException) { return Results.BadRequest(); }
        catch (OperationCanceledException) { return Results.StatusCode(408); }
        catch (Exception ex)
        {
            logger.LogError("CCVG test receiver failed with {ErrorType}, trace {TraceId} at {UtcTime}", ex.GetType().Name,
                request.HttpContext.TraceIdentifier, DateTimeOffset.UtcNow);
            return Results.StatusCode(503);
        }
    }
}
