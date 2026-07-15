using System.Net.Http.Headers;
using System.Text.Json;
using CharleyCompany.Dashboard.Web.Models;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class HousecallProDashboardDataSource(
    HttpClient httpClient,
    IOptions<HousecallProOptions> options,
    MockDashboardDataSource mockDataSource,
    DashboardNotificationService notifications,
    ILogger<HousecallProDashboardDataSource> logger) : IDashboardDataSource
{
    private readonly HousecallProOptions housecallPro = options.Value;

    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        if (!housecallPro.HasUsableApiKey && housecallPro.UseMockDataWhenApiKeyIsPlaceholder)
        {
            return await mockDataSource.GetSnapshotAsync(cancellationToken);
        }

        try
        {
            httpClient.BaseAddress = new Uri(housecallPro.BaseUrl);
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", housecallPro.ApiKey);

            var jobs = await GetJsonDocumentAsync(housecallPro.JobsEndpoint, cancellationToken);
            var estimates = await GetJsonDocumentAsync(housecallPro.EstimatesEndpoint, cancellationToken);
            var expenses = await GetJsonDocumentAsync(housecallPro.ExpensesEndpoint, cancellationToken);
            var revenue = await GetJsonDocumentAsync(housecallPro.RevenueEndpoint, cancellationToken);

            return new DashboardSnapshot(
                CurrentJobsInProgress: CountMatching(jobs, "status", "in_progress"),
                OutstandingQuotes: CountMatching(estimates, "status", "open", "sent", "pending"),
                ExpiredQuotes: CountMatching(estimates, "status", "expired"),
                MonthlyExpenses: SumMonthlyAmount(expenses),
                MonthlyRevenue: SumMonthlyAmount(revenue),
                LastUpdated: DateTimeOffset.Now,
                DataSource: "Housecall Pro API",
                RecentEvents: notifications.RecentEvents);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to load Housecall Pro dashboard data. Falling back to mock dashboard values.");
            Console.Error.WriteLine(ex);

            var fallback = await mockDataSource.GetSnapshotAsync(cancellationToken);
            return fallback with
            {
                DataSource = "Mock data - Housecall Pro API call failed; see Serilog console/file logs"
            };
        }
    }

    public async Task<VentureDashboardSnapshot> GetVentureSnapshotAsync(CancellationToken cancellationToken)
    {
        if (!housecallPro.HasUsableApiKey && housecallPro.UseMockDataWhenApiKeyIsPlaceholder)
        {
            return await mockDataSource.GetVentureSnapshotAsync(cancellationToken);
        }

        var nashvilleSnapshot = await GetSnapshotAsync(cancellationToken);
        nashvilleSnapshot = nashvilleSnapshot with
        {
            EntityName = "Charlie Company Nashville",
            EntitySlug = "nashville"
        };

        return new VentureDashboardSnapshot(
            "Charlie Company Ventures Group",
            nashvilleSnapshot with
            {
                EntityName = "Charlie Company Ventures Group",
                EntitySlug = "ventures",
                DataSource = "Housecall Pro API rollup"
            },
            [nashvilleSnapshot],
            notifications.RecentEvents);
    }

    public async Task<DashboardSnapshot?> GetEntitySnapshotAsync(string entitySlug, CancellationToken cancellationToken)
    {
        var ventureSnapshot = await GetVentureSnapshotAsync(cancellationToken);

        return ventureSnapshot.LocalEntities
            .FirstOrDefault(entity => entity.EntitySlug.Equals(entitySlug, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<JsonDocument> GetJsonDocumentAsync(string endpoint, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
    }

    private static int CountMatching(JsonDocument document, string propertyName, params string[] acceptedValues)
    {
        var count = 0;

        foreach (var item in EnumerateItems(document.RootElement))
        {
            if (TryGetString(item, propertyName, out var value)
                && acceptedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private static decimal SumMonthlyAmount(JsonDocument document)
    {
        var total = 0m;
        var now = DateTimeOffset.Now;

        foreach (var item in EnumerateItems(document.RootElement))
        {
            if (!TryGetDate(item, out var occurredAt) || occurredAt.Month != now.Month || occurredAt.Year != now.Year)
            {
                continue;
            }

            if (TryGetDecimal(item, "amount", out var amount)
                || TryGetDecimal(item, "total", out amount)
                || TryGetDecimal(item, "total_amount", out amount)
                || TryGetDecimal(item, "invoice_amount", out amount))
            {
                total += amount;
            }
        }

        return total;
    }

    private static IEnumerable<JsonElement> EnumerateItems(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        foreach (var propertyName in new[] { "data", "items", "jobs", "estimates", "quotes", "expenses" })
        {
            if (root.TryGetProperty(propertyName, out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    yield return item;
                }
            }
        }
    }

    private static bool TryGetString(JsonElement item, string propertyName, out string value)
    {
        value = string.Empty;

        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetDecimal(JsonElement item, string propertyName, out decimal value)
    {
        value = 0m;

        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out value))
        {
            return true;
        }

        return property.ValueKind == JsonValueKind.String
            && decimal.TryParse(property.GetString(), out value);
    }

    private static bool TryGetDate(JsonElement item, out DateTimeOffset date)
    {
        date = default;

        foreach (var propertyName in new[] { "created_at", "updated_at", "posted_at", "completed_at", "paid_at" })
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(property.GetString(), out date))
            {
                return true;
            }
        }

        return false;
    }
}
