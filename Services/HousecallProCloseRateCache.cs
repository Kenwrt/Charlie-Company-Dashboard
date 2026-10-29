using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed record HcpCloseRateEntry(string Id, DateTimeOffset? CreatedAt, string? Status, decimal Amount);
public sealed record HcpCloseRateState(DateTimeOffset UpdatedThrough,
    Dictionary<string, HcpCloseRateEntry> Estimates, Dictionary<string, HcpCloseRateEntry> Jobs);

public sealed class HousecallProCloseRateCache(
    IDataProtectionProvider protection, IOptions<HousecallProOptions> options,
    ILogger<HousecallProCloseRateCache> logger)
{
    private readonly ConcurrentDictionary<string, HcpCloseRateState> states = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new();
    private readonly IDataProtector protector = protection.CreateProtector("HousecallPro.CloseRateCache.v1");
    public SemaphoreSlim Gate(string slug) => gates.GetOrAdd(slug, _ => new SemaphoreSlim(1, 1));
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private string FilePath(string slug) => Path.Combine(options.Value.CloseRateCacheDirectory, Hash(slug) + ".cache");

    public async Task<HcpCloseRateState?> ReadAsync(string slug, CancellationToken token)
    {
        if (states.TryGetValue(slug, out var state)) return state;
        var path = FilePath(slug);
        if (!File.Exists(path)) return null;
        try
        {
            var encrypted = await File.ReadAllTextAsync(path, token);
            state = JsonSerializer.Deserialize<HcpCloseRateState>(protector.Unprotect(encrypted));
            if (state is not null) states.TryAdd(slug, state);
            return state;
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or JsonException)
        {
            logger.LogError(ex, "Could not read persisted close rate cache for {OperationSlug}.", slug);
            throw;
        }
    }

    public async Task SaveAsync(string slug, HcpCloseRateState state, CancellationToken token)
    {
        Directory.CreateDirectory(options.Value.CloseRateCacheDirectory);
        var path = FilePath(slug);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, protector.Protect(JsonSerializer.Serialize(state)), token);
            File.Move(temporary, path, true);
            states[slug] = state;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
