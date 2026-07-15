namespace CharleyCompany.Dashboard.Web.Options;

public sealed class HousecallProOptions
{
    public const string SectionName = "HousecallPro";

    public string BaseUrl { get; set; } = "https://api.housecallpro.com";

    public string ApiKey { get; set; } = "PLACEHOLDER_HOUSECALL_PRO_API_KEY";

    public bool UseMockDataWhenApiKeyIsPlaceholder { get; set; } = true;

    public int SyncIntervalSeconds { get; set; } = 300;

    public string JobsEndpoint { get; set; } = "/jobs";

    public string EstimatesEndpoint { get; set; } = "/estimates";

    public string ExpensesEndpoint { get; set; } = "/expenses";

    public string RevenueEndpoint { get; set; } = "/jobs";

    public bool HasUsableApiKey =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !ApiKey.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase);
}

