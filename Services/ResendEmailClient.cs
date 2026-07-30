using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class ResendEmailClient(
    HttpClient httpClient,
    IOptions<EmailOptions> options,
    ILogger<ResendEmailClient> logger)
{
    private readonly EmailOptions settings = options.Value;

    public async Task<string> SendAsync(
        string recipient,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ResendApiKey))
            throw new InvalidOperationException("Resend is not configured. Set Email:ResendApiKey.");
        if (string.IsNullOrWhiteSpace(settings.FromAddress))
            throw new InvalidOperationException("Resend is not configured. Set Email:FromAddress to an address on a verified Resend domain.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ResendApiKey);
        request.Headers.UserAgent.ParseAdd("CharlieCompany/1.0");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", $"ccv-{Guid.NewGuid():N}");
        request.Content = JsonContent.Create(new
        {
            from = string.IsNullOrWhiteSpace(settings.FromName)
                ? settings.FromAddress
                : $"{settings.FromName} <{settings.FromAddress}>",
            to = new[] { recipient },
            subject,
            html = htmlBody,
            text = textBody,
            reply_to = string.IsNullOrWhiteSpace(settings.ReplyToAddress) ? null : settings.ReplyToAddress
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Resend rejected an email to {Recipient}. Status {StatusCode}. Response: {Response}", recipient, (int)response.StatusCode, responseBody);
            throw new InvalidOperationException($"Resend rejected the email with HTTP {(int)response.StatusCode}: {ResendError(responseBody)}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var id = document.RootElement.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        logger.LogInformation("Resend accepted email {EmailId} for {Recipient}.", id, recipient);
        return id ?? "accepted";
    }

    private static string ResendError(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return document.RootElement.TryGetProperty("message", out var message)
                ? message.GetString() ?? "Unknown Resend error"
                : "Unknown Resend error";
        }
        catch (JsonException)
        {
            return "Unexpected response from Resend";
        }
    }
}
