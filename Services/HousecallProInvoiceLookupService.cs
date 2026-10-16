using System.Net.Http.Headers;
using System.Text.Json;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed record HcpInvoiceLookupRow(string Number, string Status, long Amount, long DueAmount,
    string? InvoiceDate, string? DueAt, string? PaidAt, int ItemCount, int PaymentCount);
public sealed record HcpInvoiceLookupResult(IReadOnlyList<HcpInvoiceLookupRow> Invoices,
    IReadOnlyList<string> Missing, int PagesRead, DateTimeOffset RetrievedAtUtc);
public sealed class HcpInvoiceLookupException(string message) : Exception(message) { }

// A user-triggered diagnostic read. No shared cache writes, invoice writes, or raw payload exports.
public sealed class HousecallProInvoiceLookupService(HttpClient client, IOptions<HousecallProOptions> options,
    OperationAccessService operationAccess, ILogger<HousecallProInvoiceLookupService> logger)
{
    public async Task<HcpInvoiceLookupResult> FindAsync(string operationSlug, string input,
        CancellationToken cancellationToken = default)
    {
        if (!await operationAccess.IsAdministratorAsync() ||
            !await operationAccess.CanAccessAsync(operationSlug, cancellationToken))
            throw new HcpInvoiceLookupException("You do not have access to invoice lookup for this operation.");
        var numbers = input.Split([',', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (numbers.Length is < 1 or > 10 || numbers.Any(x => x.Length > 80 || x.Any(char.IsControl)))
            throw new HcpInvoiceLookupException("Enter between one and ten invoice numbers, separated by commas.");
        var apiKey = options.Value.GetApiKey(operationSlug);
        if (apiKey is null) throw new HcpInvoiceLookupException("This operation has no configured HCP credential. No sample data was substituted.");
        if (!Uri.TryCreate(options.Value.BaseUrl, UriKind.Absolute, out var configuredBase) ||
            configuredBase.Scheme != "https" || configuredBase.Host != "api.housecallpro.com" ||
            !configuredBase.IsDefaultPort)
            throw new HcpInvoiceLookupException("Invoice lookup requires the official HCP API host.");
        var runId = Guid.NewGuid().ToString("N");
        logger.LogInformation("HCP invoice lookup started {RunId} {Operation} {RequestedCount}", runId, operationSlug, numbers.Length);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            var matches = new Dictionary<string, HcpInvoiceLookupRow>(StringComparer.Ordinal);
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            long? expectedPages = null;
            long? expectedCount = null;
            const int maxPages = 100;
            for (var page = 1; page <= maxPages; page++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"https://api.housecallpro.com/invoices?page={page}&page_size=100&sort_by=created_at&sort_direction=asc");
                // Intentionally reuse the deployed dashboard's existing Bearer convention.
                // Do not change other HCP services or try alternative authentication automatically.
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Headers.Accept.Add(new("application/json"));
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (!response.IsSuccessStatusCode)
                    throw new HcpInvoiceLookupException($"HCP returned HTTP {(int)response.StatusCode}. Lookup stopped; no automatic retry or financial write occurred.");
                await response.Content.LoadIntoBufferAsync(8 * 1024 * 1024, deadline.Token);
                using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(deadline.Token));
                var root = document.RootElement;
                var pages = root.GetProperty("total_pages").GetInt64();
                var count = root.GetProperty("total_items").GetInt64();
                if (root.GetProperty("page").GetInt32() != page || pages < 0 || pages > maxPages || count < 0 ||
                    (expectedPages is not null && expectedPages != pages) || (expectedCount is not null && expectedCount != count))
                    throw new HcpInvoiceLookupException("The invoice scan is incomplete or changed while reading. No partial result is presented as complete.");
                expectedPages = pages; expectedCount = count;
                foreach (var item in root.GetProperty("invoices").EnumerateArray())
                {
                    if (!seenIds.Add(Required(item, "id")))
                        throw new HcpInvoiceLookupException("HCP returned a repeated invoice ID during pagination. Lookup stopped.");
                    var number = Required(item, "invoice_number");
                    if (!numbers.Contains(number, StringComparer.Ordinal)) continue;
                    var row = new HcpInvoiceLookupRow(number, Required(item, "status"),
                        item.GetProperty("amount").GetInt64(), item.GetProperty("due_amount").GetInt64(),
                        Optional(item, "invoice_date"), Optional(item, "due_at"), Optional(item, "paid_at"),
                        item.GetProperty("items").GetArrayLength(), item.GetProperty("payments").GetArrayLength());
                    if (!matches.TryAdd(number, row))
                        throw new HcpInvoiceLookupException("An invoice number matches multiple records. Select a narrower company scope before continuing.");
                }
                if (page < pages) continue;
                if (seenIds.Count != count)
                    throw new HcpInvoiceLookupException("HCP pagination did not return the expected number of invoices. Lookup stopped.");
                logger.LogInformation("HCP invoice lookup completed {RunId} {Operation} {MatchedCount} {Pages}", runId, operationSlug, matches.Count, page);
                return new(numbers.Where(matches.ContainsKey).Select(x => matches[x]).ToArray(),
                    numbers.Where(x => !matches.ContainsKey(x)).ToArray(), page, DateTimeOffset.UtcNow);
            }
            throw new HcpInvoiceLookupException("The invoice scan reached its page limit. No complete result is available.");
        }
        catch (HcpInvoiceLookupException)
        {
            logger.LogWarning("HCP invoice lookup stopped {RunId} {Operation}", runId, operationSlug);
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("HCP invoice lookup cancelled or timed out {RunId} {Operation}", runId, operationSlug);
            throw new HcpInvoiceLookupException("Lookup was cancelled or exceeded its time limit. No financial write occurred.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException)
        {
            logger.LogWarning("HCP invoice lookup failed {RunId} {Operation} {ErrorType}", runId, operationSlug, ex.GetType().Name);
            throw new HcpInvoiceLookupException("HCP could not be read or returned an unexpected invoice structure. No raw response or credential was logged.");
        }
    }

    private static string Required(JsonElement item, string name) => Optional(item, name)
        ?? throw new JsonException("Required invoice field missing.");
    private static string? Optional(JsonElement item, string name) => item.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
