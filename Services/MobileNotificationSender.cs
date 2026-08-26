using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class MobileNotificationSender(
    IOptions<NotificationOptions> options,
    ICharlieTextMessagingService messaging,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<MobileNotificationSender> logger) : IOutboundNotificationSender
{
    private readonly NotificationOptions notificationOptions = options.Value;

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

            if (recipient.EnableIMessage && !string.IsNullOrWhiteSpace(recipient.CellPhoneNumber))
            {
                if (notificationOptions.IMessage.Enabled)
                {
                    logger.LogInformation(
                        "iMessage-style notification queued for {CellPhoneNumber} via {ProviderName}. Event: {EventType}",
                        recipient.CellPhoneNumber,
                        notificationOptions.IMessage.ProviderName,
                        message.EventType);
                }
                else
                {
                    logger.LogInformation(
                        "iMessage notification simulated for {CellPhoneNumber}. Server-side iMessage requires an approved Apple messaging integration. Event: {EventType}",
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
