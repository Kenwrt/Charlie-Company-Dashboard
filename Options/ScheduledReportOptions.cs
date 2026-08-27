namespace CharleyCompany.Dashboard.Web.Options;

public sealed class ScheduledReportOptions
{
    public const string SectionName = "ScheduledReports";
    public string PublicBaseUrl { get; set; } = string.Empty;
    public int PollIntervalSeconds { get; set; } = 60;
}
