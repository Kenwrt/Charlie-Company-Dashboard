namespace CharleyCompany.Dashboard.Web.Options;

public sealed class HousecallProOptions
{
    public const string SectionName = "HousecallPro";

    public string BaseUrl { get; set; } = "https://api.housecallpro.com";

    public string ApiKey { get; set; } = "PLACEHOLDER_HOUSECALL_PRO_API_KEY";

    public Dictionary<string, string> OperationApiKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public CcvgConnectionOptions Ccvg { get; set; } = new();

    public bool UseMockDataWhenApiKeyIsPlaceholder { get; set; } = true;

    public int SyncIntervalSeconds { get; set; } = 300;

    public int DashboardMaxPages { get; set; } = 2;

    public int DashboardCacheSeconds { get; set; } = 120;

    public int RecordSyncMaxPages { get; set; } = 5;

    public string CloseRateCacheDirectory { get; set; } = OperatingSystem.IsLinux()
        ? "/var/lib/charlie-company/close-rate-cache"
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CharlieCompany", "close-rate-cache");

    public string JobsEndpoint { get; set; } = "/jobs";

    public string EstimatesEndpoint { get; set; } = "/estimates";

    public string ExpensesEndpoint { get; set; } = "/expenses";

    public string RevenueEndpoint { get; set; } = "/jobs";

    public bool HasUsableApiKey =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !ApiKey.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase);

    public string? GetApiKey(string operationSlug)
    {
        // Explicit opt-in only. Never fall back to a local key if CCVG is misconfigured.
        if (Ccvg.UsesOperation(operationSlug))
            return Ccvg.Enabled && IsUsable(Ccvg.ApiKey) ? Ccvg.ApiKey : null;

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

public sealed class CcvgConnectionOptions
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public string[] OperationSlugs { get; set; } = [];
    public bool UsesOperation(string slug) => OperationSlugs.Contains(slug, StringComparer.OrdinalIgnoreCase);
}
