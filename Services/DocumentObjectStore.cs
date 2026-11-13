using System.Text.RegularExpressions;
using Amazon.S3;
using Amazon.S3.Model;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class DocumentObjectStore : IDisposable
{
    public const string WordContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private readonly AmazonS3Client? client;
    private readonly string bucket;

    public DocumentObjectStore(IConfiguration configuration)
    {
        var settings = configuration.GetSection("DocumentControl:R2");
        var accountId = settings["AccountId"];
        bucket = settings["Bucket"] ?? "";
        var accessKey = settings["AccessKeyId"];
        var secretKey = settings["SecretAccessKey"];

        if (accountId is null || !Regex.IsMatch(accountId, @"\A[a-fA-F0-9]{32}\z") ||
            !Regex.IsMatch(bucket, @"\A[a-z0-9][a-z0-9-]{1,61}[a-z0-9]\z") ||
            string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
            return;

        client = new AmazonS3Client(accessKey, secretKey, new AmazonS3Config
        {
            ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com",
            AuthenticationRegion = "auto",
            ForcePathStyle = true,
            RequestChecksumCalculation = Amazon.Runtime.RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = Amazon.Runtime.ResponseChecksumValidation.WHEN_REQUIRED,
            MaxErrorRetry = 1
        });
    }

    public bool IsConfigured => client is not null;

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var storage = client ?? throw new InvalidOperationException("Document storage is unavailable.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        await storage.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true
        }, timeout.Token);
    }

    public async Task<GetObjectResponse> GetAsync(string key, CancellationToken cancellationToken)
    {
        var storage = client ?? throw new InvalidOperationException("Document storage is unavailable.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        return await storage.GetObjectAsync(bucket, key, timeout.Token);
    }

    public void Dispose() => client?.Dispose();
}
