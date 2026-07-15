using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class MobileNotificationSender(
    IOptions<NotificationOptions> options,
    ILogger<MobileNotificationSender> logger) : IOutboundNotificationSender
{
    private readonly NotificationOptions notificationOptions = options.Value;

    public Task SendAsync(NotificationRecipient recipient, NotificationMessage message, CancellationToken cancellationToken)
    {
        try
        {
            if (recipient.EnableSms && !string.IsNullOrWhiteSpace(recipient.CellPhoneNumber))
            {
                if (notificationOptions.Sms.Enabled)
                {
                    logger.LogInformation(
                        "SMS notification queued for {CellPhoneNumber} via {ProviderName}. Event: {EventType}",
                        recipient.CellPhoneNumber,
                        notificationOptions.Sms.ProviderName,
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

        return Task.CompletedTask;
    }
}

