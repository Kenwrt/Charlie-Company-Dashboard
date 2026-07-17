using System.Net;
using System.Net.Mail;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class SmtpIdentityEmailSender(IOptions<EmailOptions> options) : IEmailSender<ApplicationUser>
{
    private readonly EmailOptions settings = options.Value;

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        SendAsync(email, "Confirm your Charlie Ventures account", $"Confirm your account by <a href=\"{confirmationLink}\">clicking here</a>.");

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        SendAsync(email, "Reset your Charlie Ventures password", $"Set or reset your password by <a href=\"{resetLink}\">clicking here</a>. This link is intended only for you.");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        SendAsync(email, "Charlie Ventures password reset code", $"Your password reset code is: <strong>{WebUtility.HtmlEncode(resetCode)}</strong>");

    private async Task SendAsync(string recipient, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(settings.SmtpHost) || string.IsNullOrWhiteSpace(settings.FromAddress))
            throw new InvalidOperationException("Email service is not configured. Set Email:SmtpHost and Email:FromAddress.");

        using var message = new MailMessage { From = new MailAddress(settings.FromAddress, settings.FromName), Subject = subject, Body = htmlBody, IsBodyHtml = true };
        message.To.Add(recipient);
        using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort) { EnableSsl = settings.UseSsl };
        if (!string.IsNullOrWhiteSpace(settings.Username))
            client.Credentials = new NetworkCredential(settings.Username, settings.Password);
        await client.SendMailAsync(message);
    }
}
