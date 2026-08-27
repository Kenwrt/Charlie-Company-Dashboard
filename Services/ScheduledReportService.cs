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

    public async Task<ScheduledReportPreview> PreviewAsync(string reportType, DateOnly localDate, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var document = await BuildDocumentAsync(db, reportType, localDate, cancellationToken);
        return new(ScheduledReportFormatter.ToPlainText(document), ScheduledReportFormatter.ToHtml(document));
    }

    public async Task<ScheduledReportTestResult> SendTestAsync(
        int definitionId,
        int recipientId,
        bool sendEmail,
        bool sendSms,
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        if (!sendEmail && !sendSms)
        {
            return new(false, "Select Email, Text, or both before sending the test.", null);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var definition = await db.ScheduledReportDefinitions.SingleAsync(x => x.Id == definitionId, cancellationToken);
        var recipient = await db.NotificationRecipients.SingleAsync(x => x.Id == recipientId && x.IsActive, cancellationToken);
        var document = await BuildDocumentAsync(db, definition.ReportType, localDate, cancellationToken);
        var plainText = ScheduledReportFormatter.ToPlainText(document);
        var html = ScheduledReportFormatter.ToHtml(document);
        var run = new ScheduledReportRun
        {
            ScheduledReportDefinitionId = definition.Id,
            ScheduledLocalDate = localDate,
            IsTest = true,
            Title = $"[TEST] {definition.Name}",
            Body = ScheduledReportFormatter.Serialize(document),
            Status = "Completed",
            CompletedAt = DateTimeOffset.UtcNow
        };
        db.ScheduledReportRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        var outcomes = new List<string>();
        var delivered = false;
        if (sendEmail)
        {
            if (!recipient.EnableEmail || string.IsNullOrWhiteSpace(recipient.EmailAddress))
            {
                outcomes.Add("Email was skipped because the recipient does not have email delivery enabled with an address.");
            }
            else
            {
                await emailSender.SendAsync(
                    recipient,
                    new NotificationMessage(run.Title, plainText, "scheduled-report-test", DateTimeOffset.UtcNow, html),
                    cancellationToken);
                outcomes.Add($"Test email was submitted for delivery to {recipient.EmailAddress}.");
                delivered = true;
            }
        }

        string? reportUrl = null;
        if (sendSms)
        {
            if (!recipient.EnableSms || string.IsNullOrWhiteSpace(recipient.CellPhoneNumber))
            {
                outcomes.Add("Text was skipped because the recipient does not have text delivery enabled with a mobile number.");
            }
            else if (!textMessaging.IsConfigured)
            {
                outcomes.Add("Text was skipped because the messaging provider is not configured.");
            }
            else
            {
                var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.PhoneNumber == recipient.CellPhoneNumber && x.PhoneNumberConfirmed && x.SmsConsentGranted,
                    cancellationToken);
                if (user is null)
                {
                    outcomes.Add("Text was skipped because no Charlie Company user has this verified number with current transactional consent.");
                }
                else
                {
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
                    reportUrl = BuildReportUrl(run.Id, rawToken);
                    if (string.IsNullOrWhiteSpace(reportUrl))
                    {
                        db.ScheduledReportAccessTokens.Remove(accessToken);
                        await db.SaveChangesAsync(cancellationToken);
                        outcomes.Add("Text was skipped because the public HTTPS report URL is not configured.");
                    }
                    else
                    {
                        await textMessaging.SendOperationalAsync(
                            user.Id,
                            user.PhoneNumber!,
                            $"TEST - Charlie Company: {definition.Name} is ready.",
                            $"charlie-company:scheduled-report-test:{run.Id}:{recipient.Id}",
                            cancellationToken,
                            reportUrl,
                            "View test report");
                        outcomes.Add($"Test text was submitted for delivery to {recipient.DisplayName}.");
                        delivered = true;
                    }
                }
            }
        }

        return new(delivered, string.Join(" ", outcomes), reportUrl);
    }

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
            var document = await BuildDocumentAsync(db, definition.ReportType, localDate, cancellationToken);
            var plainText = ScheduledReportFormatter.ToPlainText(document);
            var html = ScheduledReportFormatter.ToHtml(document);
            run.Body = ScheduledReportFormatter.Serialize(document);
            run.Status = "Completed";
            run.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            foreach (var assignment in definition.Recipients.Where(x => x.NotificationRecipient.IsActive))
            {
                var recipient = assignment.NotificationRecipient;
                if (assignment.SendEmail && recipient.EnableEmail)
                {
                    await emailSender.SendAsync(recipient, new NotificationMessage(run.Title, plainText, "scheduled-report", DateTimeOffset.UtcNow, html), cancellationToken);
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

    private static async Task<ScheduledReportDocument> BuildDocumentAsync(ApplicationDbContext db, string reportType, DateOnly localDate, CancellationToken cancellationToken)
    {
        var jobs = await db.HousecallProJobs.AsNoTracking().Include(x => x.Blockers).ToListAsync(cancellationToken);
        var estimates = await db.HousecallProEstimates.AsNoTracking().ToListAsync(cancellationToken);
        var reportDate = $"Report date: {localDate:MMMM d, yyyy}";
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
            "estimate-follow-up" => BuildEstimateFollowUpReport(reportDate, estimates),
            "expired-estimates" => BuildExpiredEstimatesReport(reportDate, expiredWindowStart, localDate, expiredEstimates),
            "job-blockers" => BuildJobBlockersReport(reportDate, jobs),
            "receivables" => BuildReceivablesReport(reportDate, jobs),
            _ => BuildDailyOperationsReport(reportDate, localDate, jobs, estimates)
        };
    }

    private static ScheduledReportDocument BuildDailyOperationsReport(
        string reportDate,
        DateOnly localDate,
        IReadOnlyCollection<HousecallProJob> jobs,
        IReadOnlyCollection<HousecallProEstimate> estimates)
    {
        var activeJobs = jobs.Where(job => !string.Equals(job.WorkStatus, "completed", StringComparison.OrdinalIgnoreCase)).ToList();
        var scheduledToday = jobs.Where(job => job.ScheduledStart.HasValue && DateOnly.FromDateTime(job.ScheduledStart.Value.Date) == localDate).ToList();
        var openEstimates = estimates.Where(estimate => !string.Equals(estimate.ApprovalStatus, "approved", StringComparison.OrdinalIgnoreCase)).ToList();
        var blockers = jobs.SelectMany(job => job.Blockers).Where(blocker => blocker.ResolvedOn == null).ToList();
        var receivables = jobs.Where(job => job.OutstandingBalance > 0).ToList();
        var rows = new List<ScheduledReportRow>
        {
            Row("Active jobs", activeJobs.Count, activeJobs.Sum(job => job.JobPrice), "Scheduled, unscheduled, and in-progress work"),
            Row("Scheduled today", scheduledToday.Count, scheduledToday.Sum(job => job.JobPrice), "Jobs scheduled for the report date"),
            Row("Open estimates", openEstimates.Count, openEstimates.Sum(estimate => estimate.TotalAmount), "Estimates awaiting a decision"),
            Row("Open blockers", blockers.Count, blockers.Sum(blocker => blocker.RevenueAtRisk), "Revenue currently at risk"),
            Row("Outstanding receivables", receivables.Count, receivables.Sum(job => job.OutstandingBalance), "Uncollected customer balances"),
            new(["Total categories", "5", "See category totals", "Categories overlap, so financial values are not added together"], true)
        };
        return new("Daily operations summary", reportDate, "Current operating workload, estimates, blockers, and receivables.",
            [new("Category"), new("Records", true), new("Financial value", true), new("Description")], rows);
    }

    private static ScheduledReportDocument BuildEstimateFollowUpReport(string reportDate, IReadOnlyCollection<HousecallProEstimate> estimates)
    {
        var items = estimates.Where(estimate => estimate.InternalStatus is HousecallProEstimateStatuses.FollowUp or HousecallProEstimateStatuses.FollowUpPending)
            .OrderBy(estimate => estimate.EstimateDate).ToList();
        var rows = items.Select(estimate => new ScheduledReportRow([
            Value(estimate.EstimateNumber, "Not provided"), Value(estimate.CustomerName, "Not recorded"),
            estimate.EstimateDate?.ToString("MM/dd/yyyy") ?? "Not provided", Value(estimate.InternalStatus, "Follow up"),
            estimate.TotalAmount.ToString("C0", CultureInfo.CurrentCulture)])).ToList();
        rows.Add(new(["Total", $"{items.Count:N0} estimates", "", "", items.Sum(estimate => estimate.TotalAmount).ToString("C0", CultureInfo.CurrentCulture)], true));
        return new("Estimate follow-up", reportDate, "Estimates that require an additional customer follow-up.",
            [new("Estimate"), new("Customer"), new("Estimate date"), new("Status"), new("Amount", true)], rows);
    }

    private static ScheduledReportDocument BuildExpiredEstimatesReport(
        string reportDate,
        DateOnly windowStart,
        DateOnly windowEnd,
        IReadOnlyCollection<HousecallProEstimate> estimates)
    {
        var rows = estimates.Select(estimate => new ScheduledReportRow([
            estimate.ExpiresAt!.Value.ToString("MM/dd/yyyy"), Value(estimate.EstimateNumber, "Not provided"),
            Value(estimate.CustomerName, "Not recorded"), estimate.TotalAmount.ToString("C0", CultureInfo.CurrentCulture)])).ToList();
        rows.Add(new(["Total", $"{estimates.Count:N0} estimates", "", estimates.Sum(estimate => estimate.TotalAmount).ToString("C0", CultureInfo.CurrentCulture)], true));
        return new("Expired estimates", reportDate, $"Unapproved estimates expiring from {windowStart:MMMM d, yyyy} through {windowEnd:MMMM d, yyyy}.",
            [new("Expiration date"), new("Estimate"), new("Customer"), new("Amount", true)], rows);
    }

    private static ScheduledReportDocument BuildJobBlockersReport(string reportDate, IReadOnlyCollection<HousecallProJob> jobs)
    {
        var items = jobs.SelectMany(job => job.Blockers.Where(blocker => blocker.ResolvedOn == null).Select(blocker => (Job: job, Blocker: blocker)))
            .OrderBy(item => item.Blocker.StartedOn).ToList();
        var rows = items.Select(item => new ScheduledReportRow([
            Value(item.Job.JobNumber, "Not provided"), Value(item.Job.CustomerName, "Not recorded"), item.Blocker.BlockerType,
            item.Blocker.StartedOn.ToString("MM/dd/yyyy"), Value(item.Blocker.NextAction, "Not recorded"),
            item.Blocker.RevenueAtRisk.ToString("C0", CultureInfo.CurrentCulture)])).ToList();
        rows.Add(new(["Total", $"{items.Count:N0} blockers", "", "", "", items.Sum(item => item.Blocker.RevenueAtRisk).ToString("C0", CultureInfo.CurrentCulture)], true));
        return new("Job blockers", reportDate, "Unresolved job blockers and their associated revenue at risk.",
            [new("Job"), new("Customer"), new("Blocker"), new("Started"), new("Next action"), new("Revenue at risk", true)], rows);
    }

    private static ScheduledReportDocument BuildReceivablesReport(string reportDate, IReadOnlyCollection<HousecallProJob> jobs)
    {
        var items = jobs.Where(job => job.OutstandingBalance > 0).OrderBy(job => job.ScheduledStart).ToList();
        var rows = items.Select(job => new ScheduledReportRow([
            Value(job.JobNumber, "Not provided"), Value(job.CustomerName, "Not recorded"), Value(job.WorkStatus, "Unknown"),
            job.ScheduledStart?.ToString("MM/dd/yyyy") ?? "Not scheduled", job.JobPrice.ToString("C0", CultureInfo.CurrentCulture),
            job.OutstandingBalance.ToString("C0", CultureInfo.CurrentCulture)])).ToList();
        rows.Add(new(["Total", $"{items.Count:N0} jobs", "", "", items.Sum(job => job.JobPrice).ToString("C0", CultureInfo.CurrentCulture),
            items.Sum(job => job.OutstandingBalance).ToString("C0", CultureInfo.CurrentCulture)], true));
        return new("Outstanding receivables", reportDate, "Jobs with an uncollected customer balance.",
            [new("Job"), new("Customer"), new("Status"), new("Job date"), new("Price", true), new("Uncollected", true)], rows);
    }

    private static ScheduledReportRow Row(string category, int count, decimal amount, string description) =>
        new([category, count.ToString("N0"), amount.ToString("C0", CultureInfo.CurrentCulture), description]);

    private static string Value(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}

public sealed record ScheduledReportTestResult(bool Delivered, string Message, string? ReportUrl);
public sealed record ScheduledReportPreview(string PlainText, string Html);

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
