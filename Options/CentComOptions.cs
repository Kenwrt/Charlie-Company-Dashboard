namespace CharleyCompany.Dashboard.Web.Options;

public sealed class CentComOptions
{
    public const string SectionName = "CentCom";
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public string ChatEndpoint { get; set; } = "/api/chat";
    public int KeepAlive { get; set; } = -1;
    public bool IsConfigured =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out _) &&
        !string.IsNullOrWhiteSpace(Model);
}
