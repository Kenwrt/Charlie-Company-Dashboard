using CharleyCompany.Dashboard.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class MobileNotificationSender(
    ICharlieTextMessagingService messaging,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<MobileNotificationSender> logger) : IOutboundNotificationSender
{
    public async Task SendAsync(NotificationRecipient recipient, NotificationMessage message, CancellationToken cancellationToken)
    {
        try
        {
            if (recipient.EnableSms && !string.IsNullOrWhiteSpace(recipient.CellPhoneNumber))
            {
                if (messaging.IsConfigured)
                {
                    await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                    var eligibleUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(user =>
                        user.PhoneNumber == recipient.CellPhoneNumber &&
                        user.PhoneNumberConfirmed &&
                        user.SmsConsentGranted,
                        cancellationToken);
                    if (eligibleUser is null)
                    {
                        logger.LogWarning(
                            "Charlie Company SMS skipped for recipient {RecipientId}: no verified user with transactional consent matched the number.",
                            recipient.Id);
                        return;
                    }
                    var body = $"Charlie Company: {message.Subject}. {message.Body}";
                    await messaging.SendOperationalAsync(
                        eligibleUser.Id,
                        recipient.CellPhoneNumber,
                        body.Length <= 1000 ? body : body[..1000],
                        $"charlie-company:{message.EventType}:{recipient.Id}:{message.OccurredAt.UtcTicks}",
                        cancellationToken);
                    logger.LogInformation(
                        "Charlie Company SMS notification submitted for recipient {RecipientId}. Event: {EventType}",
                        recipient.Id,
                        message.EventType);
                }
                else
                {
                    logger.LogInformation(
                        "SMS notification simulated for {CellPhoneNumber}. Configure an SMS provider to send real text messages. Event: {EventType}",
                        recipient.CellPhoneNumber,
                        message.EventType);
                }
            }

        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Mobile notification failed for recipient {RecipientId}.", recipient.Id);
            Console.Error.WriteLine(ex);
        }
    }
}
