using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class HousecallProDashboardDataSource(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    DashboardNotificationService notifications) : IDashboardDataSource
{
    private static readonly string[] ClosedEstimateStatuses =
    [
        "canceled",
        "deleted",
        "created job from estimate",
        "complete rated",
        "complete unrated"
    ];

    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
        (await GetVentureSnapshotAsync(cancellationToken)).Rollup;

    public async Task<VentureDashboardSnapshot> GetVentureSnapshotAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var operations = await db.LocalOperations.AsNoTracking()
            .Where(operation => operation.IsActive)
            .OrderBy(operation => operation.Name)
            .ToListAsync(cancellationToken);

        var entities = new List<DashboardSnapshot>(operations.Count);
        foreach (var operation in operations)
        {
            entities.Add(await BuildOperationSnapshotAsync(db, operation, cancellationToken));
        }

        var rollup = new DashboardSnapshot(
            CurrentJobsInProgress: entities.Sum(entity => entity.CurrentJobsInProgress),
            OutstandingEstimates: entities.Sum(entity => entity.OutstandingEstimates),
            ExpiredEstimates: 0,
            MonthlyExpenses: 0,
            MonthlyRevenue: entities.Sum(entity => entity.MonthlyRevenue),
            LastUpdated: DateTimeOffset.UtcNow,
            DataSource: "CCV synchronized Housecall Pro data",
            RecentEvents: notifications.RecentEvents,
            EntityName: "Charlie Company Ventures Group",
            EntitySlug: "ventures");

        return new VentureDashboardSnapshot(
            "Charlie Company Ventures Group",
            rollup,
            entities,
            notifications.RecentEvents);
    }

    public async Task<DashboardSnapshot?> GetEntitySnapshotAsync(string entitySlug, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var operation = await db.LocalOperations.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.IsActive && item.Slug == entitySlug,
                cancellationToken);

        return operation is null
            ? null
            : await BuildOperationSnapshotAsync(db, operation, cancellationToken);
    }

    private async Task<DashboardSnapshot> BuildOperationSnapshotAsync(
        ApplicationDbContext db,
        LocalOperation operation,
        CancellationToken cancellationToken)
    {
        var year = DateTime.Today.Year;
        var yearStart = ToUtcBoundary(new DateTime(year, 1, 1));
        var nextYearStart = ToUtcBoundary(new DateTime(year + 1, 1, 1));

        var jobsInCombinedView = db.HousecallProJobs.AsNoTracking()
            .Where(job => job.LocalOperationId == operation.Id)
            .Where(job => job.InternalStatus != HousecallProEstimateStatuses.FollowUpComplete)
            .Where(job =>
                job.InternalStatus == "scheduled" ||
                job.InternalStatus == "unscheduled" ||
                job.InternalStatus == "needs scheduling" ||
                job.InternalStatus == "in progress" ||
                (job.InternalStatus == null &&
                    (job.WorkStatus == "scheduled" ||
                     job.WorkStatus == "unscheduled" ||
                     job.WorkStatus == "needs scheduling" ||
                     job.WorkStatus == "in progress")))
            .Where(job =>
                (job.ScheduledStart >= yearStart && job.ScheduledStart < nextYearStart) ||
                job.InternalStatus == "unscheduled" ||
                job.InternalStatus == "needs scheduling" ||
                (job.InternalStatus == null &&
                    (job.WorkStatus == "unscheduled" || job.WorkStatus == "needs scheduling")));

        var scheduledJobs = jobsInCombinedView.Where(job =>
            job.InternalStatus == "scheduled" ||
            (job.InternalStatus == null && job.WorkStatus == "scheduled"));

        var jobsInProgress = await jobsInCombinedView.CountAsync(cancellationToken);
        var outstandingCharges = await scheduledJobs.SumAsync(job => job.OutstandingBalance, cancellationToken);

        var outstandingEstimates = await db.HousecallProEstimates.AsNoTracking()
            .Where(estimate => estimate.LocalOperationId == operation.Id)
            .Where(estimate => estimate.EstimateDate >= yearStart && estimate.EstimateDate < nextYearStart)
            .Where(estimate => estimate.InternalStatus != HousecallProEstimateStatuses.FollowUpComplete)
            .Where(estimate => estimate.ApprovalStatus == null || estimate.ApprovalStatus == "")
            .Where(estimate => estimate.Status == null || !ClosedEstimateStatuses.Contains(estimate.Status))
            .CountAsync(cancellationToken);

        return new DashboardSnapshot(
            CurrentJobsInProgress: jobsInProgress,
            OutstandingEstimates: outstandingEstimates,
            ExpiredEstimates: 0,
            MonthlyExpenses: 0,
            MonthlyRevenue: outstandingCharges,
            LastUpdated: DateTimeOffset.UtcNow,
            DataSource: "CCV synchronized Housecall Pro data",
            RecentEvents: notifications.RecentEvents,
            EntityName: operation.EffectiveDisplayName,
            EntitySlug: operation.Slug);
    }

    private static DateTimeOffset ToUtcBoundary(DateTime value)
    {
        var date = DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);
        return new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date)).ToUniversalTime();
    }
}
