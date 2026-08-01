namespace CharleyCompany.Dashboard.Web.Options;

public sealed class SerpApiOptions
{
    public const string SectionName = "SerpApi";
    public string BaseUrl { get; set; } = "https://serpapi.com";
    public string ApiKey { get; set; } = string.Empty;
    public string Location { get; set; } = "Nashville, Tennessee, United States";
    public int MaximumResults { get; set; } = 20;
}
