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
    public sealed record RequestMessage(string Role, string Content);

    private readonly CentComOptions settings = options.Value;

    public bool IsConfigured => settings.IsConfigured;

    public async Task<string> CompleteAsync(
        IEnumerable<CentComChatMessage> conversation,
        CancellationToken cancellationToken = default)
        => await CompleteAsync(
            conversation.Select(message => new RequestMessage(message.Role, message.Content)),
            cancellationToken);

    public async Task<string> CompleteAsync(
        IEnumerable<RequestMessage> conversation,
        CancellationToken cancellationToken = default)
        => await SendAsync(conversation, requireJson: false, cancellationToken);

    public async Task<string> CompleteJsonAsync(
        IEnumerable<RequestMessage> conversation,
        CancellationToken cancellationToken = default)
        => await SendAsync(conversation, requireJson: true, cancellationToken);

    private async Task<string> SendAsync(
        IEnumerable<RequestMessage> conversation,
        bool requireJson,
        CancellationToken cancellationToken)
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

        var messages = conversation
            .Select(x => new { role = x.Role, content = x.Content })
            .ToArray();
        var isNativeOllamaEndpoint = settings.ChatEndpoint.TrimStart('/')
            .Equals("api/chat", StringComparison.OrdinalIgnoreCase);
        request.Content = isNativeOllamaEndpoint
            ? requireJson
                ? JsonContent.Create(new
                {
                    model = settings.Model,
                    messages,
                    stream = false,
                    keep_alive = settings.KeepAlive,
                    format = "json",
                    options = new { temperature = 0, num_predict = 8192 }
                })
                : JsonContent.Create(new
                {
                    model = settings.Model,
                    messages,
                    stream = false,
                    keep_alive = settings.KeepAlive
                })
            : requireJson
                ? JsonContent.Create(new
                {
                    model = settings.Model,
                    messages,
                    response_format = new { type = "json_object" },
                    temperature = 0,
                    max_tokens = 8192
                })
                : JsonContent.Create(new { model = settings.Model, messages });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("CentCom chat returned HTTP {StatusCode}.", (int)response.StatusCode);
            throw new InvalidOperationException($"CentCom returned HTTP {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("message", out var nativeMessage) &&
            nativeMessage.TryGetProperty("content", out var nativeContent))
        {
            return nativeContent.GetString()?.Trim() ?? "CentCom returned an empty response.";
        }

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
