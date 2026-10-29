using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Models;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class HousecallProCloseRateWorker(IServiceScopeFactory scopes,
    DashboardNotificationService notifications, IOptions<HousecallProOptions> options,
    ILogger<HousecallProCloseRateWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
                await using var db = await factory.CreateDbContextAsync(stoppingToken);
                var operations = await db.LocalOperations.AsNoTracking().Where(item => item.IsActive).ToListAsync(stoppingToken);
                var service = scope.ServiceProvider.GetRequiredService<HousecallProDataService>();
                foreach (var operation in operations)
                {
                    if (options.Value.GetApiKey(operation.Slug) is null) continue;
                    try
                    {
                        await service.RefreshCloseRateAsync(operation, stoppingToken);
                        await notifications.PublishAsync(new DashboardEvent("Close rate updated",
                            "Cached close rate totals were refreshed.", DateTimeOffset.UtcNow, "Background service"));
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Close rate refresh failed for {OperationSlug}; retaining saved totals.", operation.Slug);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Close rate background worker failed."); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(30, options.Value.SyncIntervalSeconds)), stoppingToken);
        }
    }
}
