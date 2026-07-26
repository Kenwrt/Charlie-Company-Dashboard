using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class CentComChatClient(
    HttpClient httpClient,
    IOptions<CentComOptions> options,
    ILogger<CentComChatClient> logger)
{
    private readonly CentComOptions settings = options.Value;

    public bool IsConfigured => settings.IsConfigured;

    public async Task<string> CompleteAsync(
        IEnumerable<CentComChatMessage> conversation,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException(
                "CentCom is not configured. Set CentCom__BaseUrl and CentCom__Model in the application environment.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(settings.BaseUrl.TrimEnd('/') + "/"), settings.ChatEndpoint.TrimStart('/')));
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }

        request.Content = JsonContent.Create(new
        {
            model = settings.Model,
            messages = conversation
                .OrderBy(x => x.CreatedAt)
                .Select(x => new { role = x.Role, content = x.Content })
                .ToArray()
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("CentCom chat returned HTTP {StatusCode}.", (int)response.StatusCode);
            throw new InvalidOperationException($"CentCom returned HTTP {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content))
        {
            return content.GetString()?.Trim() ?? "CentCom returned an empty response.";
        }

        throw new InvalidOperationException("CentCom returned an unsupported chat response.");
    }
}
