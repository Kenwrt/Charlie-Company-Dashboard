using System.Security.Claims;
using CharleyCompany.Dashboard.Web.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class OperationAccessService(AuthenticationStateProvider authenticationStateProvider, ApplicationDbContext dbContext)
{
    public async Task<bool> IsAdministratorAsync()
    {
        var user = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        return user.IsInRole(ApplicationRoles.Administrator);
    }

    public async Task<IReadOnlyList<LocalOperation>> GetAccessibleOperationsAsync(CancellationToken cancellationToken = default)
    {
        var principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        var query = dbContext.LocalOperations.AsNoTracking().Where(operation => operation.IsActive);

        if (principal.IsInRole(ApplicationRoles.Administrator))
        {
            return await query.OrderBy(operation => operation.Name).ToListAsync(cancellationToken);
        }

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return [];
        }

        return await query
            .Where(operation => operation.UserMemberships.Any(membership => membership.UserId == userId))
            .OrderBy(operation => operation.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> CanAccessAsync(string slug, CancellationToken cancellationToken = default) =>
        (await GetAccessibleOperationsAsync(cancellationToken))
            .Any(operation => operation.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
}
