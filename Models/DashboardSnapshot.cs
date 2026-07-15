namespace CharleyCompany.Dashboard.Web.Models;

public sealed record DashboardSnapshot(
    int CurrentJobsInProgress,
    int OutstandingQuotes,
    int ExpiredQuotes,
    decimal MonthlyExpenses,
    decimal MonthlyRevenue,
    DateTimeOffset LastUpdated,
    string DataSource,
    IReadOnlyList<DashboardEvent> RecentEvents,
    string EntityName = "Charlie Company Nashville",
    string EntitySlug = "nashville")
{
    public static DashboardSnapshot Empty { get; } = new(
        0,
        0,
        0,
        0m,
        0m,
        DateTimeOffset.UtcNow,
        "Starting up",
        [],
        "Charlie Company Ventures Group",
        "ventures");
}
