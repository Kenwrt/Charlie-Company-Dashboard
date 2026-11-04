namespace CharleyCompany.Dashboard.Web.Options;

public sealed class SupplierInvoiceOptions
{
    public const string SectionName = "SupplierInvoices";
    public bool ReconciliationEnabled { get; set; }
    public bool Enabled { get; set; }
    public int DecksDocksVendorId { get; set; }
    public string? IngestionKey { get; set; }
    public string? ApReadKey { get; set; }
    public bool AutoPostVerifiedInvoices { get; set; }
    public List<string> TrustedProxyAddresses { get; set; } = [];
    public string DefaultPayingEntityCode { get; set; } = "";
    public Dictionary<string, string> PayingEntities { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ventures"] = "Charlie Company Ventures Group",
        ["nashville"] = "Charlie Company Nashville (separate entity)"
    };
    // Explicit opt-in per entity. Nashville is always reporting-only.
    public List<string> AccountingExportEntityCodes { get; set; } = [];
    public Dictionary<string, string> LocationEntityCodes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nashville"] = "nashville", ["murfreesboro"] = "ventures", ["knoxville"] = "ventures"
    };
    public string DefaultLocationSlug { get; set; } = "nashville";
    public bool AllowSingleLocationDefault { get; set; } = true;
    // Locations using this supplier account, not every location seeded in the dashboard.
    public List<string> ParticipatingLocationSlugs { get; set; } = ["nashville"];
    public Dictionary<string, string[]> LocationAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nashville"] = ["NSH", "NASHVILLE"],
        ["murfreesboro"] = ["MUR", "MURFREESBORO"],
        ["knoxville"] = ["KNX", "KNOXVILLE"]
    };
    public string StoragePath { get; set; } = OperatingSystem.IsLinux()
        ? "/var/lib/charlie-company/supplier-invoices"
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CharlieCompany", "supplier-invoices");
}
