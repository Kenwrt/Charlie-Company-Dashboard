namespace CharleyCompany.Dashboard.Web.Models;

public sealed record VentureDashboardSnapshot(
    string GroupName,
    DashboardSnapshot Rollup,
    IReadOnlyList<DashboardSnapshot> LocalEntities,
    IReadOnlyList<DashboardEvent> RecentEvents)
{
    public static VentureDashboardSnapshot Empty { get; } = new(
        "Charlie Company Ventures Group",
        DashboardSnapshot.Empty,
        [],
        []);
}

