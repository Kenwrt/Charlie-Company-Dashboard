using System.Net;
using System.Net.Mail;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class EmailNotificationSender(
    IOptions<NotificationOptions> options,
    ILogger<EmailNotificationSender> logger) : IOutboundNotificationSender
{
    private readonly EmailNotificationOptions emailOptions = options.Value.Email;

    public async Task SendAsync(NotificationRecipient recipient, NotificationMessage message, CancellationToken cancellationToken)
    {
        if (!recipient.EnableEmail || string.IsNullOrWhiteSpace(recipient.EmailAddress) || !emailOptions.Enabled)
        {
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(emailOptions.SmtpHost))
            {
                logger.LogInformation(
                    "Email notification simulated for {EmailAddress}. Configure Notifications:Email:SmtpHost to send real email. Subject: {Subject}",
                    recipient.EmailAddress,
                    message.Subject);
                return;
            }

            using var mailMessage = new MailMessage(
                emailOptions.FromAddress,
                recipient.EmailAddress,
                message.Subject,
                message.Body);

            using var smtpClient = new SmtpClient(emailOptions.SmtpHost, emailOptions.SmtpPort)
            {
                EnableSsl = emailOptions.UseSsl
            };

            if (!string.IsNullOrWhiteSpace(emailOptions.UserName))
            {
                smtpClient.Credentials = new NetworkCredential(emailOptions.UserName, emailOptions.Password);
            }

            await smtpClient.SendMailAsync(mailMessage, cancellationToken);
            logger.LogInformation("Email notification sent to {EmailAddress} for {EventType}.", recipient.EmailAddress, message.EventType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Email notification failed for {EmailAddress}.", recipient.EmailAddress);
            Console.Error.WriteLine(ex);
        }
    }
}

