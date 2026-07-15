namespace CharleyCompany.Dashboard.Web.Options;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    public EmailNotificationOptions Email { get; set; } = new();

    public SmsNotificationOptions Sms { get; set; } = new();

    public IMessageNotificationOptions IMessage { get; set; } = new();
}

public sealed class EmailNotificationOptions
{
    public bool Enabled { get; set; } = true;

    public string FromAddress { get; set; } = "dashboard@charleycompany.local";

    public string SmtpHost { get; set; } = string.Empty;

    public int SmtpPort { get; set; } = 587;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool UseSsl { get; set; } = true;
}

public sealed class SmsNotificationOptions
{
    public bool Enabled { get; set; }

    public string ProviderName { get; set; } = "Configure Twilio, Azure Communication Services, or another SMS provider";
}

public sealed class IMessageNotificationOptions
{
    public bool Enabled { get; set; }

    public string ProviderName { get; set; } = "Apple Messages for Business or another approved Apple messaging integration";
}

