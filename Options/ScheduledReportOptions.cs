namespace CharleyCompany.Dashboard.Web.Options;

public sealed class ScheduledReportOptions
{
    public const string SectionName = "ScheduledReports";
    public string PublicBaseUrl { get; set; } = string.Empty;
    public int PollIntervalSeconds { get; set; } = 60;
    public int AccessLinkHours { get; set; } = 168;
}
