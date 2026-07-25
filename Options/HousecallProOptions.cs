namespace CharleyCompany.Dashboard.Web.Options;

public sealed class HousecallProOptions
{
    public const string SectionName = "HousecallPro";

    public string BaseUrl { get; set; } = "https://api.housecallpro.com";

    public string ApiKey { get; set; } = "PLACEHOLDER_HOUSECALL_PRO_API_KEY";

    public Dictionary<string, string> OperationApiKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool UseMockDataWhenApiKeyIsPlaceholder { get; set; } = true;

    public int SyncIntervalSeconds { get; set; } = 300;

    public int DashboardMaxPages { get; set; } = 2;

    public int DashboardCacheSeconds { get; set; } = 120;

    public int RecordSyncMaxPages { get; set; } = 5;

    public string JobsEndpoint { get; set; } = "/jobs";

    public string EstimatesEndpoint { get; set; } = "/estimates";

    public string ExpensesEndpoint { get; set; } = "/expenses";

    public string RevenueEndpoint { get; set; } = "/jobs";

    public bool HasUsableApiKey =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !ApiKey.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase);

    public string? GetApiKey(string operationSlug)
    {
        if (OperationApiKeys.TryGetValue(operationSlug, out var operationKey) && IsUsable(operationKey))
        {
            return operationKey;
        }

        // Backward compatibility: the original single key was always treated as Nashville's key.
        return operationSlug.Equals("nashville", StringComparison.OrdinalIgnoreCase) && HasUsableApiKey
            ? ApiKey
            : null;
    }

    private static bool IsUsable(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase);
}
