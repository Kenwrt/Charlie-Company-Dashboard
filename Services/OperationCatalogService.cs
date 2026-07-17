using CharleyCompany.Dashboard.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class OperationCatalogService(ApplicationDbContext dbContext)
{
    public async Task<IReadOnlyList<LocalOperation>> GetActiveOperationsAsync(CancellationToken cancellationToken = default) =>
        await dbContext.LocalOperations.AsNoTracking().Where(operation => operation.IsActive).OrderBy(operation => operation.Name).ToListAsync(cancellationToken);
}
