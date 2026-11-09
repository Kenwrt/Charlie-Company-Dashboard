using System.Security.Claims;
using System.Text.Json;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed record CcvgLocation(string Id, string Name);
public sealed record CcvgReadSample(string Collection, long TotalItems, int SampleCount);

// All actions are explicit Administrator actions. Discovery and samples never import records.
public sealed class CcvgConnectionService(IHttpClientFactory clients,
    IDbContextFactory<ApplicationDbContext> databases, IOptions<HousecallProOptions> options,
    AuthenticationStateProvider authentication, ILogger<CcvgConnectionService> logger)
{
    private async Task<string> RequireAdministratorAsync()
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        if (!user.IsInRole(ApplicationRoles.Administrator)) throw new UnauthorizedAccessException();
        return user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "administrator";
    }

    private HcpConnection Connection(string? companyId = null)
    {
        var settings = options.Value.Ccvg;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.ApiKey) || settings.ApiKey.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("CCVG is disabled or its protected API key is unavailable.");
        return new HcpConnection(settings.ApiKey, companyId);
    }

    public async Task<IReadOnlyList<CcvgLocation>> DiscoverAsync(CancellationToken ct = default)
    {
        var actor = await RequireAdministratorAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.housecallpro.com/company");
        Connection().Apply(request);
        using var client = clients.CreateClient("HcpCcvg");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"CCVG discovery returned HTTP {(int)response.StatusCode}.");
        await response.Content.LoadIntoBufferAsync(2 * 1024 * 1024, ct);
        using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
        if (!document.RootElement.TryGetProperty("locations", out var locations) || locations.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("CCVG discovery did not return a locations collection.");
        var result = new List<CcvgLocation>();
        foreach (var item in locations.EnumerateArray())
        {
            var id = item.GetProperty("id").GetString();
            var name = item.GetProperty("name").GetString();
            if (string.IsNullOrWhiteSpace(id) || id.Length > 160 || id.Any(char.IsControl) || string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException("CCVG discovery returned an invalid location.");
            result.Add(new(id, name));
        }
        if (result.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw new InvalidDataException("CCVG discovery returned duplicate locations.");
        logger.LogInformation("CCVG company discovery by {ActorId} returned {LocationCount} locations at {UtcTime}", actor, result.Count, DateTimeOffset.UtcNow);
        return result;
    }

    public async Task MapAsync(int operationId, string companyId, CancellationToken ct = default)
    {
        var actor = await RequireAdministratorAsync();
        var locations = await DiscoverAsync(ct);
        if (!locations.Any(x => x.Id == companyId)) throw new InvalidOperationException("Select a location accessible through CCVG.");
        await using var db = await databases.CreateDbContextAsync(ct);
        var operation = await db.LocalOperations.SingleAsync(x => x.Id == operationId, ct);
        if (await db.LocalOperations.AnyAsync(x => x.Id != operationId && x.HousecallProLocationId == companyId, ct))
            throw new InvalidOperationException("This HCP location is already assigned to another operation.");
        var previous = operation.HousecallProLocationId;
        operation.HousecallProLocationId = companyId;
        operation.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("CCVG mapping changed by {ActorId} for operation {OperationId}: {PreviousCompanyId} to {CompanyId} at {UtcTime}",
            actor, operationId, previous, companyId, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<CcvgReadSample>> ReadSampleAsync(int operationId, CancellationToken ct = default)
    {
        var actor = await RequireAdministratorAsync();
        await using var db = await databases.CreateDbContextAsync(ct);
        var operation = await db.LocalOperations.AsNoTracking().SingleAsync(x => x.Id == operationId, ct);
        if (string.IsNullOrWhiteSpace(operation.HousecallProLocationId)) throw new InvalidOperationException("Map this operation to an HCP company first.");
        var connection = Connection(operation.HousecallProLocationId);
        var samples = new List<CcvgReadSample>();
        using var client = clients.CreateClient("HcpCcvg");
        foreach (var collection in new[] { "jobs", "estimates" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.housecallpro.com/{collection}?page=1&page_size=1");
            connection.Apply(request);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"CCVG sample returned HTTP {(int)response.StatusCode}.");
            await response.Content.LoadIntoBufferAsync(2 * 1024 * 1024, ct);
            using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
            connection.ValidateCompany(document.RootElement);
            samples.Add(new(collection, document.RootElement.GetProperty("total_items").GetInt64(), document.RootElement.GetProperty(collection).GetArrayLength()));
        }
        logger.LogInformation("CCVG read-only sample completed by {ActorId} for operation {OperationId} at {UtcTime}", actor, operationId, DateTimeOffset.UtcNow);
        return samples;
    }
}
