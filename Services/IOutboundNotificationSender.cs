using CharleyCompany.Dashboard.Web.Data;

namespace CharleyCompany.Dashboard.Web.Services;

public interface IOutboundNotificationSender
{
    Task SendAsync(NotificationRecipient recipient, NotificationMessage message, CancellationToken cancellationToken);
}

