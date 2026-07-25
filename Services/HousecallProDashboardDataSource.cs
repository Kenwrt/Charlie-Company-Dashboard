using System.Net.Http.Headers;
using System.Text.Json;
using CharleyCompany.Dashboard.Web.Models;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class HousecallProDashboardDataSource(
    HttpClient httpClient,
    IOptions<HousecallProOptions> options,
    IMemoryCache cache,
    MockDashboardDataSource mockDataSource,
    DashboardNotificationService notifications,
    ILogger<HousecallProDashboardDataSource> logger) : IDashboardDataSource
{
    private readonly HousecallProOptions housecallPro = options.Value;

    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
        (await GetVentureSnapshotAsync(cancellationToken)).Rollup;

    public async Task<VentureDashboardSnapshot> GetVentureSnapshotAsync(CancellationToken cancellationToken) =>
        (await cache.GetOrCreateAsync("housecallpro:venture-dashboard", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Math.Max(30, housecallPro.DashboardCacheSeconds));
            return GetVentureSnapshotCoreAsync(cancellationToken);
        }))!;

    private async Task<VentureDashboardSnapshot> GetVentureSnapshotCoreAsync(CancellationToken cancellationToken)
    {
        var baseline = await mockDataSource.GetVentureSnapshotAsync(cancellationToken);
        var entities = new List<DashboardSnapshot>(baseline.LocalEntities.Count);

        foreach (var entity in baseline.LocalEntities)
        {
            var apiKey = housecallPro.GetApiKey(entity.EntitySlug);
            if (apiKey is null)
            {
                entities.Add(entity);
                continue;
            }

            entities.Add(await GetOperationSnapshotAsync(entity, apiKey, cancellationToken));
        }

        var liveCount = entities.Count(x => x.DataSource.StartsWith("Housecall Pro", StringComparison.OrdinalIgnoreCase));
        var rollup = new DashboardSnapshot(
            CurrentJobsInProgress: entities.Sum(x => x.CurrentJobsInProgress),
            OutstandingEstimates: entities.Sum(x => x.OutstandingEstimates),
            ExpiredEstimates: entities.Sum(x => x.ExpiredEstimates),
            MonthlyExpenses: entities.Sum(x => x.MonthlyExpenses),
            MonthlyRevenue: entities.Sum(x => x.MonthlyRevenue),
            LastUpdated: DateTimeOffset.Now,
            DataSource: liveCount == 0 ? baseline.Rollup.DataSource : $"Mixed rollup - {liveCount} Housecall Pro operation(s), {entities.Count - liveCount} mock operation(s)",
            RecentEvents: notifications.RecentEvents,
            EntityName: "Charlie Company Ventures Group",
            EntitySlug: "ventures");

        return new VentureDashboardSnapshot("Charlie Company Ventures Group", rollup, entities, notifications.RecentEvents);
    }

    public async Task<DashboardSnapshot?> GetEntitySnapshotAsync(string entitySlug, CancellationToken cancellationToken) =>
        (await GetVentureSnapshotAsync(cancellationToken)).LocalEntities
            .FirstOrDefault(entity => entity.EntitySlug.Equals(entitySlug, StringComparison.OrdinalIgnoreCase));

    private async Task<DashboardSnapshot> GetOperationSnapshotAsync(DashboardSnapshot baseline, string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            var jobs = await GetAllItemsAsync(housecallPro.JobsEndpoint, "jobs", apiKey, cancellationToken);
            var estimates = await GetAllItemsAsync(housecallPro.EstimatesEndpoint, "estimates", apiKey, cancellationToken);
            using var expenses = await GetOptionalJsonDocumentAsync(housecallPro.ExpensesEndpoint, apiKey, cancellationToken);

            return new DashboardSnapshot(
                CurrentJobsInProgress: CountMatching(jobs, "work_status", "in progress"),
                OutstandingEstimates: CountEstimates(estimates, expired: false),
                ExpiredEstimates: CountEstimates(estimates, expired: true),
                MonthlyExpenses: expenses is null ? 0m : SumMonthlyAmount(expenses),
                MonthlyRevenue: SumMonthlyJobRevenue(jobs),
                LastUpdated: DateTimeOffset.Now,
                DataSource: $"Housecall Pro API - {baseline.EntityName} (recent {jobs.Count} jobs / {estimates.Count} estimates)",
                RecentEvents: notifications.RecentEvents,
                EntityName: baseline.EntityName,
                EntitySlug: baseline.EntitySlug);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to load Housecall Pro dashboard data for operation {OperationSlug}; retaining mock values.", baseline.EntitySlug);
            return baseline with { DataSource = $"Mock data - Housecall Pro call failed for {baseline.EntityName}" };
        }
    }

    private async Task<JsonDocument> GetJsonDocumentAsync(string endpoint, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(housecallPro.BaseUrl), endpoint));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task<List<JsonElement>> GetAllItemsAsync(string endpoint, string collectionName, string apiKey, CancellationToken cancellationToken)
    {
        var items = new List<JsonElement>();
        var page = 1;
        var totalPages = 1;
        do
        {
            var separator = endpoint.Contains('?') ? '&' : '?';
            using var document = await GetJsonDocumentAsync($"{endpoint}{separator}page={page}&page_size=100", apiKey, cancellationToken);
            if (document.RootElement.TryGetProperty(collectionName, out var collection) && collection.ValueKind == JsonValueKind.Array)
                items.AddRange(collection.EnumerateArray().Select(item => item.Clone()));
            if (document.RootElement.TryGetProperty("total_pages", out var pages) && pages.TryGetInt32(out var parsedPages))
                totalPages = Math.Max(1, parsedPages);
            page++;
        } while (page <= Math.Min(totalPages, Math.Max(1, housecallPro.DashboardMaxPages)));
        return items;
    }

    private async Task<JsonDocument?> GetOptionalJsonDocumentAsync(string endpoint, string apiKey, CancellationToken cancellationToken)
    {
        try { return await GetJsonDocumentAsync(endpoint, apiKey, cancellationToken); }
        catch (HttpRequestException ex) { logger.LogWarning(ex, "Optional Housecall Pro endpoint {Endpoint} was unavailable.", endpoint); return null; }
    }

    private static int CountMatching(IEnumerable<JsonElement> items, string propertyName, params string[] acceptedValues) =>
        items.Count(item => TryGetString(item, propertyName, out var value) && acceptedValues.Contains(value, StringComparer.OrdinalIgnoreCase));

    private static int CountEstimates(IEnumerable<JsonElement> estimates, bool expired) => estimates.Count(estimate =>
    {
        if (!estimate.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array) return false;
        return options.EnumerateArray().Any(option =>
        {
            TryGetString(option, "approval_status", out var approval);
            TryGetString(option, "status", out var status);
            if (expired) return approval.Equals("expired", StringComparison.OrdinalIgnoreCase);
            return string.IsNullOrWhiteSpace(approval) && !new[] { "canceled", "deleted", "created job from estimate", "complete rated", "complete unrated" }.Contains(status, StringComparer.OrdinalIgnoreCase);
        });
    });

    private static decimal SumMonthlyJobRevenue(IEnumerable<JsonElement> jobs)
    {
        var now = DateTimeOffset.Now;
        return jobs.Where(job =>
            job.TryGetProperty("work_timestamps", out var timestamps) &&
            timestamps.ValueKind == JsonValueKind.Object &&
            TryGetDateProperty(timestamps, "completed_at", out var completedAt) &&
            completedAt.Month == now.Month && completedAt.Year == now.Year)
            .Sum(job => TryGetDecimal(job, "total_amount", out var cents) ? cents / 100m : 0m);
    }

    private static decimal SumMonthlyAmount(JsonDocument document)
    {
        var now = DateTimeOffset.Now;
        return EnumerateItems(document.RootElement)
            .Where(item => TryGetDate(item, out var occurredAt) && occurredAt.Month == now.Month && occurredAt.Year == now.Year)
            .Sum(item => TryGetDecimal(item, "amount", out var amount) || TryGetDecimal(item, "total", out amount) || TryGetDecimal(item, "total_amount", out amount) || TryGetDecimal(item, "invoice_amount", out amount) ? amount : 0m);
    }

    private static IEnumerable<JsonElement> EnumerateItems(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) { foreach (var item in root.EnumerateArray()) yield return item; yield break; }
        foreach (var name in new[] { "data", "items", "jobs", "estimates", "quotes", "expenses" })
            if (root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
                foreach (var item in array.EnumerateArray()) yield return item;
    }

    private static bool TryGetString(JsonElement item, string name, out string value)
    {
        value = string.Empty;
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var property)) return false;
        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetDecimal(JsonElement item, string name, out decimal value)
    {
        value = 0m;
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var property)) return false;
        return property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out value) || property.ValueKind == JsonValueKind.String && decimal.TryParse(property.GetString(), out value);
    }

    private static bool TryGetDate(JsonElement item, out DateTimeOffset date)
    {
        date = default;
        foreach (var name in new[] { "created_at", "updated_at", "posted_at", "completed_at", "paid_at" })
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(property.GetString(), out date)) return true;
        return false;
    }

    private static bool TryGetDateProperty(JsonElement item, string name, out DateTimeOffset date)
    {
        date = default;
        return item.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(property.GetString(), out date);
    }
}
