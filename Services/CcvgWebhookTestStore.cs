using System.Text.Json;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed record CcvgWebhookReceipt(string Fingerprint, DateTimeOffset ReceivedAtUtc,
    string EventType, string CompanyHash, int? OperationId, int Bytes, string[] RootFields);

// Separate, bounded metadata store. No payloads, secrets, imports or notifications.
// One writer process per directory; separate deployments must use separate directories.
public sealed class CcvgWebhookTestStore(IOptions<CcvgWebhookTestOptions> options)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string FilePath => Path.Combine(options.Value.Directory, "receipts.json");
    private async Task<List<CcvgWebhookReceipt>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(FilePath)) return [];
        if (new FileInfo(FilePath).Length > 8 * 1024 * 1024) throw new IOException("CCVG receipt store exceeds its limit.");
        await using var stream = File.OpenRead(FilePath);
        var rows = await JsonSerializer.DeserializeAsync<List<CcvgWebhookReceipt>>(stream, cancellationToken: ct) ?? [];
        return rows.Where(x => x.ReceivedAtUtc >= DateTimeOffset.UtcNow.AddHours(-Math.Clamp(options.Value.RetentionHours, 1, 168)))
            .OrderByDescending(x => x.ReceivedAtUtc).Take(Math.Clamp(options.Value.MaxEvents, 1, 1000)).ToList();
    }
    public async Task<bool> AddAsync(CcvgWebhookReceipt receipt, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var rows = await LoadAsync(ct);
            if (rows.Any(x => x.Fingerprint == receipt.Fingerprint)) return false;
            rows.Insert(0, receipt);
            rows = rows.Take(Math.Clamp(options.Value.MaxEvents, 1, 1000)).ToList();
            System.IO.Directory.CreateDirectory(options.Value.Directory);
            var temporary = FilePath + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(rows), ct);
            File.Move(temporary, FilePath, true);
            return true;
        }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<CcvgWebhookReceipt>> ReadAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { return await LoadAsync(ct); }
        finally { gate.Release(); }
    }

    public async Task PruneAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(FilePath)) return;
            var rows = await LoadAsync(ct);
            var temporary = FilePath + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(rows), ct);
            File.Move(temporary, FilePath, true);
        }
        finally { gate.Release(); }
    }
}

public sealed class CcvgWebhookRetentionWorker(CcvgWebhookTestStore store,
    IOptions<CcvgWebhookTestOptions> options, ILogger<CcvgWebhookRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try { await store.PruneAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError("CCVG metadata retention failed with {ErrorType} at {UtcTime}", ex.GetType().Name, DateTimeOffset.UtcNow); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
