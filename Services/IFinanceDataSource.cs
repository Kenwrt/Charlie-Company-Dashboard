using CharleyCompany.Dashboard.Web.Models;

namespace CharleyCompany.Dashboard.Web.Services;

public interface IFinanceDataSource
{
    Task<VentureFinanceDashboard> GetVentureFinanceDashboardAsync(CancellationToken cancellationToken);

    Task<FinanceDashboard?> GetEntityFinanceDashboardAsync(string entitySlug, CancellationToken cancellationToken);
}

