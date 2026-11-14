using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class DocumentWorkingDrive(HttpClient client, IConfiguration configuration)
{
    private const string Api = "https://www.googleapis.com/drive/v3/files";
    private const string StsUrl = "https://sts.googleapis.com/v1/token";
    private const string Subject = "api01-document-control";
    private const string DriveScope = "https://www.googleapis.com/auth/drive.file";
    private readonly string? folder = configuration["DocumentControl:Drive:FolderId"];
    private readonly string? keyPath = configuration["DocumentControl:Drive:WorkloadKeyPath"];
    private readonly string? issuer = configuration["DocumentControl:Drive:WorkloadIssuer"];
    private readonly string? audience = configuration["DocumentControl:Drive:WorkloadAudience"];
    private readonly string? account = configuration["DocumentControl:Drive:ServiceAccountEmail"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(folder) &&
        !string.IsNullOrWhiteSpace(keyPath) && !string.IsNullOrWhiteSpace(issuer) &&
        !string.IsNullOrWhiteSpace(audience) && !string.IsNullOrWhiteSpace(account);

    public async Task<string> ReserveIdAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Api}/generateIds?count=1");
        using var response = await SendAsync(request, await TokenAsync(cancellationToken), cancellationToken);
        response.EnsureSuccessStatusCode();
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        return body!.RootElement.GetProperty("ids")[0].GetString()!;
    }

    public async Task CreateBlankAsync(string id, string name, CancellationToken cancellationToken)
    {
        var token = await TokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&supportsAllDrives=true");
        var content = new MultipartContent("related");
        content.Add(JsonContent.Create(new { id, name, parents = new[] { folder }, mimeType = DocumentObjectStore.WordContentType }));
        var file = new ByteArrayContent(CreateBlankWord());
        file.Headers.ContentType = new MediaTypeHeaderValue(DocumentObjectStore.WordContentType);
        content.Add(file);
        request.Content = content;
        using var response = await SendAsync(request, token, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            await ReadMetadataAsync(id, token, cancellationToken);
            return;
        }
        response.EnsureSuccessStatusCode();
    }

    public async Task<byte[]> ReadWordAsync(string id, CancellationToken cancellationToken)
    {
        var token = await TokenAsync(cancellationToken);
        var before = await ReadMetadataAsync(id, token, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Api}/{Uri.EscapeDataString(id)}?alt=media&supportsAllDrives=true");
        using var response = await SendAsync(request, token, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) != 0)
        {
            if (buffer.Length + read > WordUpload.MaxBytes)
                throw new InvalidDataException("Working file exceeds the upload limit.");
            buffer.Write(chunk, 0, read);
        }
        var after = await ReadMetadataAsync(id, token, cancellationToken);
        if (before != after)
            throw new InvalidDataException("Working file changed during submission.");
        return buffer.ToArray();
    }

    private async Task<string> ReadMetadataAsync(string id, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{Api}/{Uri.EscapeDataString(id)}?supportsAllDrives=true&fields=parents,mimeType,trashed,version");
        using var response = await SendAsync(request, token, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        var item = body!.RootElement;
        if (item.GetProperty("trashed").GetBoolean() ||
            item.GetProperty("mimeType").GetString() != DocumentObjectStore.WordContentType ||
            !item.GetProperty("parents").EnumerateArray().Any(parent => parent.GetString() == folder))
            throw new InvalidDataException("Working file must be a Word file in the configured folder.");
        return item.GetProperty("version").GetString()!;
    }

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string token, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private async Task<string> TokenAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            alg = "RS256", typ = "JWT", kid = Subject
        }));
        var claims = WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = issuer,
            sub = Subject,
            aud = "https:" + audience,
            iat = now,
            exp = now + 300
        }));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(await File.ReadAllTextAsync(keyPath!, cancellationToken));
        var message = $"{header}.{claims}";
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(message), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
            ["audience"] = audience!,
            ["scope"] = "https://www.googleapis.com/auth/cloud-platform",
            ["requested_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
            ["subject_token_type"] = "urn:ietf:params:oauth:token-type:jwt",
            ["subject_token"] = $"{message}.{WebEncoders.Base64UrlEncode(signature)}"
        });
        using var response = await client.PostAsync(StsUrl, form, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        var federated = body!.RootElement.GetProperty("access_token").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://iamcredentials.googleapis.com/v1/projects/-/serviceAccounts/{Uri.EscapeDataString(account!)}:generateAccessToken");
        request.Content = JsonContent.Create(new { scope = new[] { DriveScope }, lifetime = "600s" });
        using var impersonated = await SendAsync(request, federated, cancellationToken);
        impersonated.EnsureSuccessStatusCode();
        using var token = await impersonated.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        return token!.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static byte[] CreateBlankWord()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);
            Write("_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Write("word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p/></w:body></w:document>
                """);

            void Write(string path, string xml)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(xml);
            }
        }
        return buffer.ToArray();
    }
}
