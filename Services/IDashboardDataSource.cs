using CharleyCompany.Dashboard.Web.Models;

namespace CharleyCompany.Dashboard.Web.Services;

public interface IDashboardDataSource
{
    Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    Task<VentureDashboardSnapshot> GetVentureSnapshotAsync(CancellationToken cancellationToken);

    Task<DashboardSnapshot?> GetEntitySnapshotAsync(string entitySlug, CancellationToken cancellationToken);
}
