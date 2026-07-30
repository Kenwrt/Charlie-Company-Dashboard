using CharleyCompany.Dashboard.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class CentComTaskAnalysisWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<CentComTaskAnalysisWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("CentCom task analysis worker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int? jobId;
                using (var scope = scopeFactory.CreateScope())
                {
                    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
                    await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                    jobId = await db.QuoteProcessingJobs
                        .AsNoTracking()
                        .Where(job => job.JobType == "CentCom Task Analysis" && job.Status == "Queued")
                        .OrderBy(job => job.CreatedAt)
                        .Select(job => (int?)job.Id)
                        .FirstOrDefaultAsync(stoppingToken);
                }

                if (jobId is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                using var processingScope = scopeFactory.CreateScope();
                var processor = processingScope.ServiceProvider.GetRequiredService<CentComTaskAnalysisService>();
                await processor.ProcessAsync(jobId.Value, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unexpected CentCom task analysis worker error.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
