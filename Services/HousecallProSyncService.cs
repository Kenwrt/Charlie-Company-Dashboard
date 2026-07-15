using CharleyCompany.Dashboard.Web.Models;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class HousecallProSyncService(
    IServiceProvider services,
    DashboardNotificationService notifications,
    IOptions<HousecallProOptions> options,
    ILogger<HousecallProSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(30, options.Value.SyncIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var dataSource = scope.ServiceProvider.GetRequiredService<IDashboardDataSource>();
                await dataSource.GetSnapshotAsync(stoppingToken);

                await notifications.PublishAsync(new DashboardEvent(
                    "Sync completed",
                    "Dashboard metrics were refreshed.",
                    DateTimeOffset.Now,
                    "Background service"));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Housecall Pro background sync failed.");
                Console.Error.WriteLine(ex);
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}

