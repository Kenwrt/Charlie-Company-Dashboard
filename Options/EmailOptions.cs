namespace CharleyCompany.Dashboard.Web.Options;

public sealed class EmailOptions
{
    public const string SectionName = "Email";
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Charlie Ventures";
}
