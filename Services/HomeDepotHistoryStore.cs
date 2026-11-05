using System.Security.Cryptography;
using System.Text.Json;
using CharleyCompany.Dashboard.Web.Models;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

// Private originals plus immutable state revisions. One file lock across processes; no shared-DB migration.
// The current state is published by atomic rename only after originals and revision are durable.
public sealed class HomeDepotHistoryStore(IOptions<HomeDepotHistoryOptions> options, IWebHostEnvironment environment)
{
    public const int MaxBytes = 20 * 1024 * 1024;
    private string Root()
    {
        var settings = options.Value;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.AccountScope)) throw new InvalidOperationException("Home Depot history is disabled or its account scope is not configured.");
        var root = Path.GetFullPath(settings.StoragePath);
        foreach (var forbidden in new[] { environment.ContentRootPath, environment.WebRootPath })
            if (!string.IsNullOrWhiteSpace(forbidden) && (root.Equals(Path.GetFullPath(forbidden), StringComparison.OrdinalIgnoreCase) || root.StartsWith(Path.GetFullPath(forbidden).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Home Depot storage must be outside the application and web roots.");
        Directory.CreateDirectory(root);
        Private(root, true);
        return root;
    }
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Private(string path, bool directory = false)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | (directory ? UnixFileMode.UserExecute : 0));
    }
    private static async Task WriteNewAsync(string path, byte[] bytes, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        Private(path);
        await stream.WriteAsync(bytes, ct);
        stream.Flush(true);
    }
    private async Task<FileStream> LockAsync(string root, CancellationToken ct)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(15);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { var stream = new FileStream(Path.Combine(root, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); Private(stream.Name); return stream; }
            catch (IOException) when (DateTimeOffset.UtcNow < until) { await Task.Delay(100, ct); }
        }
    }
    private async Task<HomeDepotHistoryState> LoadAsync(string root, CancellationToken ct)
    {
        var path = Path.Combine(root, "current.json");
        var state = File.Exists(path) ? JsonSerializer.Deserialize<HomeDepotHistoryState>(await File.ReadAllBytesAsync(path, ct), HomeDepotHistoryText.Json)! : new HomeDepotHistoryState { AccountScope = options.Value.AccountScope };
        if (state.SchemaVersion != 1 || state.AccountScope != options.Value.AccountScope) throw new InvalidOperationException("History storage account/schema mismatch; use a separate directory for another account.");
        return state;
    }
    public async Task<T> ChangeAsync<T>(Func<HomeDepotHistoryState, Task<T>> action, CancellationToken ct)
    {
        var root = Root();
        await using var gate = await LockAsync(root, ct);
        var state = await LoadAsync(root, ct);
        var result = await action(state);
        state.Revision++;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, HomeDepotHistoryText.Json);
        var revisions = Path.Combine(root, "revisions"); Directory.CreateDirectory(revisions); Private(revisions, true);
        await WriteNewAsync(Path.Combine(revisions, $"{state.Revision:D16}-{Guid.NewGuid():N}.json"), bytes, ct);
        var temporary = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tmp");
        await WriteNewAsync(temporary, bytes, ct);
        File.Move(temporary, Path.Combine(root, "current.json"), true);
        return result;
    }
    public async Task<HomeDepotHistoryState> ReadAsync(string actor, CancellationToken ct) => await ChangeAsync(state =>
    {
        state.Audit.Add(new(DateTimeOffset.UtcNow, actor, "Read", null, "", "", "Private history accessed"));
        return Task.FromResult(state);
    }, ct);
    public async Task SaveOriginalAsync(string hash, byte[] bytes, CancellationToken ct)
    {
        var path = Path.Combine(Root(), hash + ".source");
        if (!File.Exists(path))
        {
            var temporary = Path.Combine(Root(), Guid.NewGuid().ToString("N") + ".tmp");
            await WriteNewAsync(temporary, bytes, ct);
            try { File.Move(temporary, path); }
            catch (IOException) when (File.Exists(path)) { File.Delete(temporary); }
        }
    }
    public async Task<(byte[] Bytes, string Type)> DocumentAsync(string id, string actor, CancellationToken ct)
    {
        var document = await ChangeAsync(state =>
        {
            var found = state.Documents.SingleOrDefault(x => x.Id == id) ?? throw new ArgumentException("Document not found.");
            state.Audit.Add(new(DateTimeOffset.UtcNow, actor, "DocumentRead", found.PurchaseId, "", id, "Private original accessed"));
            return Task.FromResult(found);
        }, ct);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(Root(), document.Sha256 + ".source"), ct);
        if (Hash(bytes) != document.Sha256) throw new InvalidDataException("Stored original integrity failure.");
        return (bytes, document.Kind == "Csv" ? "text/csv" : "application/pdf");
    }
}
