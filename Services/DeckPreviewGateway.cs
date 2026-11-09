using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class DeckPreviewGateway(HttpClient http, IConfiguration configuration)
{
    public bool Enabled => configuration.GetValue<bool>("DeckPreviews:Enabled")
        && Uri.TryCreate(configuration["DeckPreviews:GatewayUrl"], UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.AbsolutePath == "/" && uri.UserInfo == ""
        && uri.Query == "" && uri.Fragment == ""
        && !string.IsNullOrWhiteSpace(configuration["DeckPreviews:GatewayApiKey"]);

    public async Task<string> UploadAsync(Stream photo, string contentType)
    {
        using var body = new StreamContent(photo);
        body.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var response = await SendAsync(HttpMethod.Post, "/api/v2/deck-assets", body);
        var asset = await response.Content.ReadFromJsonAsync<Asset>();
        return ObjectId(asset?.Id);
    }

    public Task<Job> SubmitAsync(Guid key, string body) =>
        JobAsync(HttpMethod.Post, "/api/v3/google-deck-jobs", new StringContent(body, Encoding.UTF8, "application/json"), key);

    public Task<Job> PollAsync(string id, bool cancel) => JobAsync(cancel ? HttpMethod.Post : HttpMethod.Get,
        $"/api/v3/google-deck-jobs/{ObjectId(id)}{(cancel ? "/cancel" : "")}");

    public async Task<byte[]> OutputAsync(string id)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/v1/objects/{ObjectId(id)}");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (response.Content.Headers.ContentType?.MediaType != "image/png" ||
            !bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidDataException("Unexpected preview image format.");
        return bytes;
    }

    private async Task<Job> JobAsync(HttpMethod method, string path, HttpContent? body = null, Guid? key = null)
    {
        using var response = await SendAsync(method, path, body, key);
        var job = await response.Content.ReadFromJsonAsync<Job>();
        if (job is null || job.State is not ("Queued" or "Running" or "Succeeded" or "Failed" or "Cancelled" or "NeedsReview"))
            throw new InvalidDataException("Unexpected preview job state.");
        ObjectId(job.Id);
        return job;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? body = null, Guid? key = null)
    {
        if (!Enabled) throw new InvalidOperationException("Design previews are disabled.");
        using var request = new HttpRequestMessage(method, new Uri(new Uri(configuration["DeckPreviews:GatewayUrl"]!), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration["DeckPreviews:GatewayApiKey"]);
        request.Content = body;
        if (key.HasValue) request.Headers.Add("Idempotency-Key", key.Value.ToString("N"));
        var response = await http.SendAsync(request);
        if (response.IsSuccessStatusCode) return response;

        using (response)
        {
            string? code = null;
            try
            {
                var problem = await response.Content.ReadFromJsonAsync<Problem>();
                code = problem?.Title;
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException) { }

            throw new Rejection(code switch
            {
                "google_upload_consent_required" => "Google upload consent is required.",
                "idempotency_conflict" => "The gateway reported an idempotency conflict. Resolve it before generating another preview.",
                "google_queue_full" => "The Google preview queue is full.",
                "google_pilot_call_limit" => "The Google pilot reservation limit has been reached.",
                "google_key_not_configured" => "The gateway has no Google credential configured.",
                _ => $"The gateway returned HTTP {(int)response.StatusCode}."
            }, code is "google_queue_full" or "google_pilot_call_limit" or "google_upload_consent_required"
                or "google_key_not_configured" or "invalid_google_request");
        }
    }

    private static string ObjectId(string? id) => Guid.TryParseExact(id, "N", out var parsed)
        ? parsed.ToString("N") : throw new InvalidDataException("Invalid gateway object identifier.");

    private sealed record Asset(string Id);
    private sealed record Problem(string Title);
    public sealed record Job(string Id, string State, string? OutputObjectId, string? Error);
    public sealed record Submission(string PhotoObjectId, string Design, bool AuthorizeGoogleUpload, string ConsentReference);
    public sealed class Rejection(string message, bool definite) : Exception(message)
    {
        public bool Definite { get; } = definite;
    }
}
