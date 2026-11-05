namespace CharleyCompany.Dashboard.Web.Options;

public sealed class HomeDepotHistoryOptions
{
    public const string SectionName = "HomeDepotHistory";
    public bool Enabled { get; set; }
    // Opaque identifier for this purchasing account, not a card number or customer name.
    public string AccountScope { get; set; } = "";
    public string? IngestionKey { get; set; }
    public string? AccountingReadKey { get; set; }
    public List<string> TrustedProxyAddresses { get; set; } = [];
    // Explicit, independently confirmed operation-to-HCP-company mapping. No umbrella fallback.
    public Dictionary<int, string> OperationCompanyIds { get; set; } = [];
    public string StoragePath { get; set; } = OperatingSystem.IsLinux()
        ? "/var/lib/charlie-company/home-depot-history"
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CharlieCompany", "home-depot-history");
}
