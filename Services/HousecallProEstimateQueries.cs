using CharleyCompany.Dashboard.Web.Data;

namespace CharleyCompany.Dashboard.Web.Services;

internal static class HousecallProEstimateQueries
{
    private static readonly string[] ClosedStatuses =
    [
        "canceled", "deleted", "created job from estimate", "complete rated", "complete unrated"
    ];

    public static IQueryable<HousecallProEstimate> OutstandingForYear(
        this IQueryable<HousecallProEstimate> estimates, int year)
    {
        var start = ToUtcBoundary(new DateTime(year, 1, 1));
        var end = ToUtcBoundary(new DateTime(year + 1, 1, 1));
        return estimates
            .Where(estimate => estimate.LocalOperation.IsActive)
            .Where(estimate => estimate.EstimateDate >= start && estimate.EstimateDate < end)
            .Where(estimate => estimate.InternalStatus != HousecallProEstimateStatuses.FollowUpComplete)
            .Where(estimate => estimate.ApprovalStatus == null || estimate.ApprovalStatus == "")
            .Where(estimate => estimate.Status == null || !ClosedStatuses.Contains(estimate.Status));
    }

    private static DateTimeOffset ToUtcBoundary(DateTime value)
    {
        var date = DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);
        return new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date)).ToUniversalTime();
    }
}