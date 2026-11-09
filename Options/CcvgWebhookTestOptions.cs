namespace CharleyCompany.Dashboard.Web.Options;

public sealed class CcvgWebhookTestOptions
{
    public bool Enabled { get; set; }
    public bool AllowRegistrationChallenge { get; set; }
    public string SigningSecret { get; set; } = "";
    public int TimestampToleranceSeconds { get; set; } = 300;
    public int MaxBodyBytes { get; set; } = 262144;
    public int RetentionHours { get; set; } = 24;
    public int MaxEvents { get; set; } = 1000;
    public string Directory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CharlieCompany", "ccvg-webhook-test");
}
