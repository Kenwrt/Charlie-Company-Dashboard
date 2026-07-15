using CharleyCompany.Dashboard.Web.Models;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class MockDashboardDataSource(DashboardNotificationService notifications) : IDashboardDataSource
{
    public Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(BuildVentureSnapshot().Rollup);
    }

    public Task<VentureDashboardSnapshot> GetVentureSnapshotAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(BuildVentureSnapshot());
    }

    public Task<DashboardSnapshot?> GetEntitySnapshotAsync(string entitySlug, CancellationToken cancellationToken)
    {
        var snapshot = BuildVentureSnapshot()
            .LocalEntities
            .FirstOrDefault(entity => entity.EntitySlug.Equals(entitySlug, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(snapshot);
    }

    private VentureDashboardSnapshot BuildVentureSnapshot()
    {
        var now = DateTimeOffset.Now;
        var daySeed = DateTime.Today.Day;
        var localEntities = new List<DashboardSnapshot>
        {
            CreateEntity("Charlie Company Nashville", "nashville", 18, 42, 7, 18450.75m, 97200.00m, daySeed, now),
            CreateEntity("Charlie Company Knoxville", "knoxville", 11, 25, 4, 12180.25m, 62450.00m, daySeed + 2, now),
            CreateEntity("Charlie Company Chattanooga", "chattanooga", 9, 19, 3, 9785.40m, 51780.00m, daySeed + 4, now)
        };

        var rollup = new DashboardSnapshot(
            CurrentJobsInProgress: localEntities.Sum(entity => entity.CurrentJobsInProgress),
            OutstandingQuotes: localEntities.Sum(entity => entity.OutstandingQuotes),
            ExpiredQuotes: localEntities.Sum(entity => entity.ExpiredQuotes),
            MonthlyExpenses: localEntities.Sum(entity => entity.MonthlyExpenses),
            MonthlyRevenue: localEntities.Sum(entity => entity.MonthlyRevenue),
            LastUpdated: now,
            DataSource: "Mock venture rollup - add Housecall Pro API keys per local entity to enable live API calls",
            RecentEvents: notifications.RecentEvents,
            EntityName: "Charlie Company Ventures Group",
            EntitySlug: "ventures");

        return new VentureDashboardSnapshot(
            "Charlie Company Ventures Group",
            rollup,
            localEntities,
            notifications.RecentEvents);
    }

    private DashboardSnapshot CreateEntity(
        string entityName,
        string entitySlug,
        int jobsBase,
        int quotesBase,
        int expiredBase,
        decimal expensesBase,
        decimal revenueBase,
        int seed,
        DateTimeOffset now)
    {
        return new DashboardSnapshot(
            CurrentJobsInProgress: jobsBase + seed % 6,
            OutstandingQuotes: quotesBase + seed % 9,
            ExpiredQuotes: expiredBase + seed % 4,
            MonthlyExpenses: expensesBase + seed * 137.25m,
            MonthlyRevenue: revenueBase + seed * 945.50m,
            LastUpdated: now,
            DataSource: "Mock local entity data",
            RecentEvents: notifications.RecentEvents,
            EntityName: entityName,
            EntitySlug: entitySlug);
    }
}
