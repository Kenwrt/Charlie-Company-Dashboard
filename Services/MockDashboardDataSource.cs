using CharleyCompany.Dashboard.Web.Models;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class MockDashboardDataSource(DashboardNotificationService notifications, OperationCatalogService operationCatalog) : IDashboardDataSource
{
    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        return (await BuildVentureSnapshotAsync(cancellationToken)).Rollup;
    }

    public Task<VentureDashboardSnapshot> GetVentureSnapshotAsync(CancellationToken cancellationToken)
    {
        return BuildVentureSnapshotAsync(cancellationToken);
    }

    public async Task<DashboardSnapshot?> GetEntitySnapshotAsync(string entitySlug, CancellationToken cancellationToken)
    {
        var snapshot = (await BuildVentureSnapshotAsync(cancellationToken))
            .LocalEntities
            .FirstOrDefault(entity => entity.EntitySlug.Equals(entitySlug, StringComparison.OrdinalIgnoreCase));

        return snapshot;
    }

    private async Task<VentureDashboardSnapshot> BuildVentureSnapshotAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        var daySeed = DateTime.Today.Day;
        var operations = await operationCatalog.GetActiveOperationsAsync(cancellationToken);
        var localEntities = operations.Select((operation, index) =>
        {
            var seed = Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(operation.Slug));
            return CreateEntity(operation.Name, operation.Slug, 8 + seed % 12, 15 + seed % 25, 2 + seed % 6,
                9000m + seed % 12000, 45000m + seed % 65000, daySeed + index, now);
        }).ToList();

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
