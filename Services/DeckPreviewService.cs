using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CharleyCompany.Dashboard.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class DeckPreviewService(
    IDbContextFactory<ApplicationDbContext> factory,
    OperationAccessService access,
    DeckPreviewGateway gateway,
    IDataProtectionProvider protection,
    IConfiguration configuration,
    IWebHostEnvironment environment)
{
    private const int MaxPhotoBytes = 8 * 1024 * 1024;
    private readonly IDataProtector protector = protection.CreateProtector("CharlieCompany.DeckPreviews.v1");

    private string? StorageRoot
    {
        get
        {
            var path = configuration["DeckPreviews:StoragePath"];
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return null;
            var root = Path.GetFullPath(path);
            return Within(root, environment.ContentRootPath)
                || (environment.WebRootPath is not null && Within(root, environment.WebRootPath)) ? null : root;
        }
    }

    public bool Enabled => gateway.Enabled && StorageRoot is not null;

    public static bool IsDeck(string taskType) => taskType is "Deck" or "Covered Deck / Roof Structure";

    public static string Design(QuoteProjectTask task, EstimateOption option)
    {
        var pattern = DeckPatterns.All.FirstOrDefault(item => item.Id == option.DeckPatternId).Name;
        return $"""
            Create a photographic deck design preview from this single overview. Preserve the camera viewpoint,
            house, patio, deck footprint, stair and landing geometry, and railing openings unless the saved scope
            or option explicitly changes them. Remove plants and furniture only where requested.
            Use recorded measurements. Leave unspecified geometry and finishes as seen in the photograph.
            Work: {option.WorkType ?? task.WorkType}
            Decking: {option.AutomaticMaterial ?? "As specified in the option and scope"}
            Board layout or border: {pattern ?? "As specified in the option and scope"}
            Railing: {(option.IncludeRailing ? "Include the specified railing" : "No new railing selected. Preserve existing railing unless scope specifies a change.")}
            Saved option finish, color, and construction details: {option.Description}
            Saved scope including stair, railing, and site changes: {task.ScopeOfWork}
            Estimator measurements: {task.Measurements}
            """;
    }

    public static string Signature(string design) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(design)));

    public async Task<IReadOnlyList<Preview>> ListAsync(ClaimsPrincipal user, Context key)
    {
        await LoadAsync(user, key);
        var directory = DirectoryFor(key);
        return ReadAll(directory).OrderByDescending(item => item.ConsentedAt).Select(item => Project(directory, item)).ToArray();
    }

    public async Task<Preview> GenerateAsync(ClaimsPrincipal user, Context key, int photoId,
        string revision, string inputSignature, bool authorized, string consentReference)
    {
        if (!authorized || string.IsNullOrWhiteSpace(consentReference) || consentReference.Trim().Length > 512)
            throw new PreviewException("Confirm Google upload and enter a customer approval reference of up to 512 characters.");
        var actor = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actor)) throw Unavailable();

        var directory = DirectoryFor(key);
        await LoadAsync(user, key);
        using var gate = Lock(directory);
        var saved = await LoadAsync(user, key);
        if (!saved.Editable || saved.Revision != revision || Signature(saved.Design) != inputSignature)
            throw new PreviewException("The saved estimate changed. Reload it before authorizing a preview.");
        if (saved.Design.Length > 4000)
            throw new PreviewException("The saved design exceeds the gateway's 4,000-character limit. Shorten the option details or scope.");
        if (ReadAll(directory).Any(item => item.State is "Queued" or "Running" || item.CanReconcile))
            throw new PreviewException("Resolve the outstanding submission for this option before starting another preview.");

        var photo = FindPhoto(saved.Task, photoId);
        await using var source = OpenPhoto(photo);
        if (source.Length > MaxPhotoBytes)
            throw new PreviewException("Choose an original photo within the gateway's 8 MiB upload limit.");
        var contentType = PhotoType(source);
        string asset;
        try { asset = await gateway.UploadAsync(source, contentType); }
        catch (DeckPreviewGateway.Rejection ex) { throw new PreviewException(ex.Message); }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw new PreviewException("The photo upload did not complete. No generation request was sent.");
        }

        var body = new DeckPreviewGateway.Submission(asset, saved.Design, true, consentReference.Trim());
        var record = new Record(Guid.NewGuid(), photoId, saved.Revision, inputSignature, actor,
            DateTimeOffset.UtcNow, JsonSerializer.Serialize(body, JsonSerializerOptions.Web));
        Save(directory, record);
        return await SubmitAsync(directory, record);
    }

    public async Task<Preview> UpdateAsync(ClaimsPrincipal user, Context key, Guid id, Action action)
    {
        var directory = DirectoryFor(key);
        await LoadAsync(user, key);
        using var gate = Lock(directory);
        var saved = await LoadAsync(user, key);
        var record = Read(directory, id) ?? throw Unavailable();
        var photo = FindPhoto(saved.Task, record.PhotoId);
        var current = saved.IsLatest && record.InputSignature == Signature(saved.Design);

        if (action == Action.Review)
        {
            if (!current || record.State != "Succeeded" || !File.Exists(OutputPath(directory, id)))
                throw new PreviewException("Review requires a current preview with a saved image.");
            using var original = OpenPhoto(photo);
            PhotoType(original);
            record.ReviewedAt = DateTimeOffset.UtcNow;
            record.ReviewedBy = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw Unavailable();
            Save(directory, record);
            return Project(directory, record);
        }

        if (action == Action.Reconcile)
        {
            if (!record.CanReconcile || !current || !saved.Editable)
                throw new PreviewException("This submission cannot be reconciled with the current estimate.");
            return await SubmitAsync(directory, record, reconciling: true);
        }

        if (record.GatewayJobId is null) throw Unavailable();
        if (action == Action.Cancel && record.State is not ("Queued" or "Running"))
            throw new PreviewException("Only queued or running previews can be cancelled.");
        if (record.State == "Succeeded" && File.Exists(OutputPath(directory, id))) return Project(directory, record);

        try
        {
            if (record.State != "Succeeded")
            {
                var job = await gateway.PollAsync(record.GatewayJobId, action == Action.Cancel);
                if (job.Id != record.GatewayJobId) throw new InvalidDataException("Unexpected gateway job identifier.");
                ApplyJob(directory, record, job);
            }
            await SaveOutputAsync(directory, record);
            return Project(directory, record);
        }
        catch (DeckPreviewGateway.Rejection ex) { throw new PreviewException(ex.Message); }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw new PreviewException("The gateway update or image retrieval did not complete. Refresh to check the recorded job.");
        }
    }

    public async Task<IResult> ImageAsync(ClaimsPrincipal user, Context key, Guid id, bool original, bool download)
    {
        try
        {
            var saved = await LoadAsync(user, key);
            var directory = DirectoryFor(key);
            var record = Read(directory, id) ?? throw Unavailable();
            var photo = FindPhoto(saved.Task, record.PhotoId);
            if (!original)
            {
                if (record.State != "Succeeded" || (download && (record.ReviewedAt is null
                    || !saved.IsLatest || record.InputSignature != Signature(saved.Design)))) return Results.NotFound();
                var path = OutputPath(directory, id);
                return File.Exists(path)
                    ? Results.File(protector.Unprotect(await File.ReadAllBytesAsync(path)), "image/png",
                        download ? "deck-design-preview.png" : null)
                    : Results.NotFound();
            }
            var stream = OpenPhoto(photo);
            try { return Results.File(stream, PhotoType(stream)); }
            catch { stream.Dispose(); throw; }
        }
        catch (Exception ex) when (ex is PreviewException or IOException or UnauthorizedAccessException or CryptographicException)
        {
            return Results.NotFound();
        }
    }

    private async Task<Preview> SubmitAsync(string directory, Record record, bool reconciling = false)
    {
        DeckPreviewGateway.Job job;
        try { job = await gateway.SubmitAsync(record.Id, record.Body); }
        catch (DeckPreviewGateway.Rejection ex)
        {
            record.CanReconcile = reconciling || !ex.Definite;
            record.State = record.CanReconcile ? "NeedsReview" : "Rejected";
            record.Message = ex.Message;
            Save(directory, record);
            return Project(directory, record);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            record.State = "NeedsReview";
            record.Message = "Submission outcome is unknown and may have incurred a charge. Reconcile with the same request.";
            Save(directory, record);
            return Project(directory, record);
        }

        ApplyJob(directory, record, job);
        return Project(directory, record);
    }

    private void ApplyJob(string directory, Record record, DeckPreviewGateway.Job job)
    {
        record.GatewayJobId = job.Id;
        record.CanReconcile = false;
        record.State = job.State;
        record.OutputObjectId = job.OutputObjectId;
        record.Message = job.State switch
        {
            "NeedsReview" => "Google processing stopped with an uncertain outcome. A provider charge may have occurred.",
            "Failed" when job.Error == "google_no_image_or_safety_block" => "Google returned no image or blocked the request.",
            "Failed" => "Google could not complete the preview.",
            "Cancelled" => "Cancellation is best effort. Google may already have processed and billed the request.",
            _ => null
        };
        Save(directory, record);
    }

    private async Task SaveOutputAsync(string directory, Record record)
    {
        if (record.State != "Succeeded" || File.Exists(OutputPath(directory, record.Id))) return;
        var bytes = await gateway.OutputAsync(record.OutputObjectId ?? throw new InvalidDataException("Missing preview output."));
        WriteAtomic(OutputPath(directory, record.Id), protector.Protect(bytes));
    }

    private async Task<SavedInputs> LoadAsync(ClaimsPrincipal user, Context key)
    {
        if (!Enabled || user.Identity?.IsAuthenticated != true
            || !(user.IsInRole(ApplicationRoles.Administrator) || user.IsInRole(ApplicationRoles.LocalOperator)))
            throw Unavailable();
        var operations = (await access.GetAccessibleOperationsAsync(user)).Select(operation => operation.Id).ToArray();
        await using var db = await factory.CreateDbContextAsync();
        var version = await db.QuoteVersions.AsNoTracking().Where(item => item.Id == key.VersionId
            && item.QuoteCaseId == key.QuoteId && operations.Contains(item.QuoteCase.LocalOperationId))
            .Select(item => new { item.OptionsJson, item.Status, item.ApprovedAt, item.VersionNumber }).SingleOrDefaultAsync();
        if (version?.OptionsJson is null) throw Unavailable();
        var task = await db.QuoteProjectTasks.AsNoTracking().Include(item => item.Photos)
            .SingleOrDefaultAsync(item => item.Id == key.TaskId && item.QuoteCaseId == key.QuoteId && !item.IsDeleted);
        if (task is null || !IsDeck(task.TaskType)) throw Unavailable();
        var options = EstimateOptions.Read(version.OptionsJson);
        var option = options.Tasks.SingleOrDefault(item => item.TaskId == key.TaskId)?.Options
            .SingleOrDefault(item => item.Id == key.OptionId) ?? throw Unavailable();
        var latest = !await db.QuoteVersions.AnyAsync(item => item.QuoteCaseId == key.QuoteId
            && item.VersionNumber > version.VersionNumber);
        return new(task, Signature(version.OptionsJson), Design(task, option), latest, latest && version.Status == "Draft" && version.ApprovedAt is null);
    }

    private string DirectoryFor(Context key) => Path.Combine(StorageRoot ?? throw Unavailable(),
        $"estimate-{key.QuoteId}", $"revision-{key.VersionId}", $"task-{key.TaskId}", key.OptionId.ToString("N"));

    private static QuoteProjectTaskPhoto FindPhoto(QuoteProjectTask task, int id) =>
        task.Photos.SingleOrDefault(photo => photo.Id == id) ?? throw Unavailable();

    private FileStream OpenPhoto(QuoteProjectTaskPhoto photo)
    {
        var root = configuration["EstimatePictures:StoragePath"];
        if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(environment.ContentRootPath, "EstimatePictures");
        var path = Path.GetFullPath(photo.StoragePath);
        if (!Within(path, root)) throw Unavailable();
        return File.OpenRead(path);
    }

    private static string PhotoType(Stream stream)
    {
        Span<byte> header = stackalloc byte[12];
        var length = stream.ReadAtLeast(header, header.Length, false);
        stream.Position = 0;
        if (length == 12)
        {
            if (header.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
            if (header.StartsWith(new byte[] { 255, 216, 255 })) return "image/jpeg";
            if (header[..4].SequenceEqual("RIFF"u8) && header[8..].SequenceEqual("WEBP"u8)) return "image/webp";
        }
        throw new PreviewException("Choose a JPEG, PNG, or WebP photo for the preview.");
    }

    private static bool Within(string path, string root)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar);
    }

    private static FileStream Lock(string directory)
    {
        Directory.CreateDirectory(directory);
        try { return File.Open(Path.Combine(directory, "write.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None); }
        catch (IOException) { throw new PreviewException("This option already has a preview operation in progress."); }
    }

    private IEnumerable<Record> ReadAll(string directory) => Directory.Exists(directory)
        ? Directory.EnumerateFiles(directory, "*.preview").Select(path => Deserialize(path))
        : [];

    private Record? Read(string directory, Guid id)
    {
        var path = RecordPath(directory, id);
        return File.Exists(path) ? Deserialize(path) : null;
    }

    private Record Deserialize(string path) =>
        JsonSerializer.Deserialize<Record>(protector.Unprotect(File.ReadAllBytes(path)))
        ?? throw new InvalidDataException("Preview record is missing.");

    private void Save(string directory, Record record) =>
        WriteAtomic(RecordPath(directory, record.Id), protector.Protect(JsonSerializer.SerializeToUtf8Bytes(record)));

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: true);
    }

    private static string RecordPath(string directory, Guid id) => Path.Combine(directory, $"{id:N}.preview");
    private static string OutputPath(string directory, Guid id) => Path.Combine(directory, $"{id:N}.image");
    private static Preview Project(string directory, Record record) =>
        new(record.Id, record.PhotoId, record.InputSignature, record.State, record.Message, record.CanReconcile,
            record.GatewayJobId is not null, record.ConsentedAt, record.ReviewedAt,
            record.State == "Succeeded" && File.Exists(OutputPath(directory, record.Id)));
    private static bool IsTransportFailure(Exception ex) =>
        ex is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException;
    private static PreviewException Unavailable() => new("This estimate, photo, or preview is unavailable.");

    private sealed record SavedInputs(QuoteProjectTask Task, string Revision, string Design, bool IsLatest, bool Editable);
    private sealed record Record(Guid Id, int PhotoId, string Revision, string InputSignature,
        string ConsentedBy, DateTimeOffset ConsentedAt, string Body)
    {
        public string State { get; set; } = "NeedsReview";
        public string? Message { get; set; }
        public bool CanReconcile { get; set; } = true;
        public string? GatewayJobId { get; set; }
        public string? OutputObjectId { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
        public string? ReviewedBy { get; set; }
    }

    public sealed record Context(int QuoteId, int VersionId, int TaskId, Guid OptionId);
    public sealed record Preview(Guid Id, int PhotoId, string InputSignature, string State, string? Message,
        bool CanReconcile, bool HasGatewayJob, DateTimeOffset ConsentedAt, DateTimeOffset? ReviewedAt, bool HasImage);
    public enum Action { Refresh, Cancel, Reconcile, Review }
    public sealed class PreviewException(string message) : Exception(message) { }
}
