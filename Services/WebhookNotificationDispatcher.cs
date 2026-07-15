using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class WebhookNotificationDispatcher(
    ApplicationDbContext dbContext,
    IEnumerable<IOutboundNotificationSender> senders,
    ILogger<WebhookNotificationDispatcher> logger)
{
    public async Task DispatchAsync(DashboardEvent dashboardEvent, CancellationToken cancellationToken)
    {
        try
        {
            var recipients = await dbContext.NotificationRecipients
                .AsNoTracking()
                .Where(recipient => recipient.IsActive)
                .ToListAsync(cancellationToken);

            var matchingRecipients = recipients
                .Where(recipient => ShouldNotify(recipient, dashboardEvent.EventType))
                .ToList();

            if (matchingRecipients.Count == 0)
            {
                logger.LogInformation("No notification recipients matched webhook event {EventType}.", dashboardEvent.EventType);
                return;
            }

            var message = new NotificationMessage(
                $"Charley Company: {dashboardEvent.EventType}",
                $"{dashboardEvent.Message}{Environment.NewLine}{Environment.NewLine}Source: {dashboardEvent.Source}{Environment.NewLine}Received: {dashboardEvent.OccurredAt.LocalDateTime:g}",
                dashboardEvent.EventType,
                dashboardEvent.OccurredAt);

            foreach (var recipient in matchingRecipients)
            {
                foreach (var sender in senders)
                {
                    await sender.SendAsync(recipient, message, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook notification dispatch failed for event {EventType}.", dashboardEvent.EventType);
            Console.Error.WriteLine(ex);
        }
    }

    private static bool ShouldNotify(NotificationRecipient recipient, string eventType)
    {
        var isQuoteEvent = eventType.Contains("quote", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("estimate", StringComparison.OrdinalIgnoreCase);
        var isExpenseEvent = eventType.Contains("expense", StringComparison.OrdinalIgnoreCase);

        return (isQuoteEvent && recipient.NotifyOnQuoteEvents)
            || (isExpenseEvent && recipient.NotifyOnExpenseEvents)
            || (!isQuoteEvent && !isExpenseEvent);
    }
}

