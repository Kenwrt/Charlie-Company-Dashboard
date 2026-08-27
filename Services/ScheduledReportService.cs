using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

public static class ScheduledReportCatalog
{
    public static readonly IReadOnlyDictionary<string, string> Types = new Dictionary<string, string>
    {
        ["daily-operations"] = "Daily operations summary",
        ["estimate-follow-up"] = "Estimate follow-up",
        ["expired-estimates"] = "Expired estimates, last two months",
        ["job-blockers"] = "Job blockers",
        ["receivables"] = "Outstanding receivables"
    };
}

public sealed class ScheduledReportService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    EmailNotificationSender emailSender,
    ICharlieTextMessagingService textMessaging,
    IOptions<ScheduledReportOptions> options,
    ILogger<ScheduledReportService> logger)
{
    private readonly ScheduledReportOptions reportOptions = options.Value;

    public async Task ExecuteAsync(int definitionId, DateOnly localDate, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var definition = await db.ScheduledReportDefinitions
            .Include(x => x.Recipients).ThenInclude(x => x.NotificationRecipient)
            .SingleAsync(x => x.Id == definitionId, cancellationToken);

        var run = new ScheduledReportRun
        {
            ScheduledReportDefinitionId = definition.Id,
            ScheduledLocalDate = localDate,
            Title = definition.Name,
            Body = "Preparing report"
        };
        db.ScheduledReportRuns.Add(run);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            logger.LogInformation("Scheduled report {ReportId} was already claimed for {LocalDate}.", definitionId, localDate);
            return;
        }

        try
        {
            run.Body = await BuildBodyAsync(db, definition.ReportType, localDate, cancellationToken);
            run.Status = "Completed";
            run.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            foreach (var assignment in definition.Recipients.Where(x => x.NotificationRecipient.IsActive))
            {
                var recipient = assignment.NotificationRecipient;
                if (assignment.SendEmail && recipient.EnableEmail)
                {
                    await emailSender.SendAsync(recipient, new NotificationMessage(run.Title, run.Body, "scheduled-report", DateTimeOffset.UtcNow), cancellationToken);
                }

                if (assignment.SendSms && recipient.EnableSms && textMessaging.IsConfigured)
                {
                    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x =>
                        x.PhoneNumber == recipient.CellPhoneNumber && x.PhoneNumberConfirmed && x.SmsConsentGranted,
                        cancellationToken);
                    if (user is null)
                    {
                        logger.LogWarning("Scheduled report SMS skipped for recipient {RecipientId}: verified transactional consent was not found.", recipient.Id);
                        continue;
                    }

                    var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
                    var accessToken = new ScheduledReportAccessToken
                    {
                        ScheduledReportRunId = run.Id,
                        NotificationRecipientId = recipient.Id,
                        TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))),
                        ExpiresAt = DateTimeOffset.UtcNow.AddHours(Math.Clamp(reportOptions.AccessLinkHours, 1, 720))
                    };
                    db.ScheduledReportAccessTokens.Add(accessToken);
                    await db.SaveChangesAsync(cancellationToken);
                    var reportUrl = BuildReportUrl(run.Id, rawToken);
                    if (string.IsNullOrWhiteSpace(reportUrl))
                    {
                        db.ScheduledReportAccessTokens.Remove(accessToken);
                        await db.SaveChangesAsync(cancellationToken);
                        continue;
                    }

                    await textMessaging.SendOperationalAsync(
                        user.Id,
                        user.PhoneNumber!,
                        $"Charlie Company: {run.Title} is ready.",
                        $"charlie-company:scheduled-report:{run.Id}:{recipient.Id}",
                        cancellationToken,
                        reportUrl,
                        "View report");
                }
            }
        }
        catch (Exception ex)
        {
            run.Status = "Failed";
            run.Error = ex.Message.Length <= 500 ? ex.Message : ex.Message[..500];
            run.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogError(ex, "Scheduled report {ReportId} failed.", definitionId);
        }
    }

    private string? BuildReportUrl(long runId, string token)
    {
        if (!Uri.TryCreate(reportOptions.PublicBaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
        {
            logger.LogWarning("Scheduled report text link skipped because ScheduledReports:PublicBaseUrl is not a valid HTTPS URL.");
            return null;
        }

        var reportUri = new Uri(baseUri, $"reports/runs/{runId}");
        return QueryHelpers.AddQueryString(reportUri.AbsoluteUri, "token", token);
    }

    private static async Task<string> BuildBodyAsync(ApplicationDbContext db, string reportType, DateOnly localDate, CancellationToken cancellationToken)
    {
        var jobs = await db.HousecallProJobs.AsNoTracking().Include(x => x.Blockers).ToListAsync(cancellationToken);
        var estimates = await db.HousecallProEstimates.AsNoTracking().ToListAsync(cancellationToken);
        var heading = $"Report date: {localDate:MMMM d, yyyy}";
        var expiredWindowStart = localDate.AddMonths(-2);
        var expiredEstimates = estimates
            .Where(x => x.ExpiresAt.HasValue)
            .Where(x => DateOnly.FromDateTime(x.ExpiresAt!.Value.Date) >= expiredWindowStart)
            .Where(x => DateOnly.FromDateTime(x.ExpiresAt!.Value.Date) <= localDate)
            .Where(x => !string.Equals(x.ApprovalStatus, "approved", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.ExpiresAt)
            .ToList();
        return reportType switch
        {
            "estimate-follow-up" => $"{heading}{Environment.NewLine}{Environment.NewLine}Estimates requiring follow-up: {estimates.Count(x => x.InternalStatus == HousecallProEstimateStatuses.FollowUp || x.InternalStatus == HousecallProEstimateStatuses.FollowUpPending):N0}{Environment.NewLine}Open estimate value: {estimates.Where(x => !string.Equals(x.ApprovalStatus, "approved", StringComparison.OrdinalIgnoreCase)).Sum(x => x.TotalAmount).ToString("C0", CultureInfo.CurrentCulture)}",
            "expired-estimates" => BuildExpiredEstimatesReport(heading, expiredWindowStart, localDate, expiredEstimates),
            "job-blockers" => $"{heading}{Environment.NewLine}{Environment.NewLine}Open blockers: {jobs.Sum(x => x.Blockers.Count(b => b.ResolvedOn == null)):N0}{Environment.NewLine}Revenue at risk: {jobs.SelectMany(x => x.Blockers).Where(x => x.ResolvedOn == null).Sum(x => x.RevenueAtRisk).ToString("C0", CultureInfo.CurrentCulture)}",
            "receivables" => $"{heading}{Environment.NewLine}{Environment.NewLine}Jobs with outstanding balances: {jobs.Count(x => x.OutstandingBalance > 0):N0}{Environment.NewLine}Outstanding total: {jobs.Sum(x => x.OutstandingBalance).ToString("C0", CultureInfo.CurrentCulture)}",
            _ => $"{heading}{Environment.NewLine}{Environment.NewLine}Active jobs: {jobs.Count(x => !string.Equals(x.WorkStatus, "completed", StringComparison.OrdinalIgnoreCase)):N0}{Environment.NewLine}Scheduled today: {jobs.Count(x => x.ScheduledStart.HasValue && DateOnly.FromDateTime(x.ScheduledStart.Value.Date) == localDate):N0}{Environment.NewLine}Open estimates: {estimates.Count(x => !string.Equals(x.ApprovalStatus, "approved", StringComparison.OrdinalIgnoreCase)):N0}{Environment.NewLine}Open blockers: {jobs.Sum(x => x.Blockers.Count(b => b.ResolvedOn == null)):N0}{Environment.NewLine}Outstanding receivables: {jobs.Sum(x => x.OutstandingBalance).ToString("C0", CultureInfo.CurrentCulture)}"
        };
    }

    private static string BuildExpiredEstimatesReport(
        string heading,
        DateOnly windowStart,
        DateOnly windowEnd,
        IReadOnlyCollection<HousecallProEstimate> estimates)
    {
        var body = new StringBuilder()
            .AppendLine(heading)
            .AppendLine()
            .AppendLine($"Expiration window: {windowStart:MMMM d, yyyy} through {windowEnd:MMMM d, yyyy}")
            .AppendLine($"Expired estimates: {estimates.Count:N0}")
            .AppendLine($"Expired estimate value: {estimates.Sum(x => x.TotalAmount).ToString("C0", CultureInfo.CurrentCulture)}");

        if (estimates.Count > 0)
        {
            body.AppendLine();
            foreach (var estimate in estimates)
            {
                var number = string.IsNullOrWhiteSpace(estimate.EstimateNumber) ? "No estimate number" : estimate.EstimateNumber;
                var customer = string.IsNullOrWhiteSpace(estimate.CustomerName) ? "Customer not recorded" : estimate.CustomerName;
                body.AppendLine($"{estimate.ExpiresAt!.Value:MMM d, yyyy} | {number} | {customer} | {estimate.TotalAmount.ToString("C0", CultureInfo.CurrentCulture)}");
            }
        }

        return body.ToString().TrimEnd();
    }
}

public sealed class ScheduledReportWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ScheduledReportOptions> options,
    ILogger<ScheduledReportWorker> logger) : BackgroundService
{
    private readonly ScheduledReportOptions reportOptions = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(reportOptions.PollIntervalSeconds, 30, 900)));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
                await using var db = await factory.CreateDbContextAsync(stoppingToken);
                var definitions = await db.ScheduledReportDefinitions.AsNoTracking().Where(x => x.IsActive).ToListAsync(stoppingToken);
                foreach (var definition in definitions)
                {
                    var zone = TimeZoneInfo.FindSystemTimeZoneById(definition.TimeZoneId);
                    var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
                    if (TimeOnly.FromDateTime(localNow.DateTime) < definition.RunAtLocalTime) continue;
                    await scope.ServiceProvider.GetRequiredService<ScheduledReportService>()
                        .ExecuteAsync(definition.Id, DateOnly.FromDateTime(localNow.Date), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "Scheduled report polling failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
