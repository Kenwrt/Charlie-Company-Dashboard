using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class HomeDepotCatalogLookupService(
    HttpClient httpClient,
    IOptions<SerpApiOptions> options,
    IMemoryCache cache,
    ILogger<HomeDepotCatalogLookupService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly SerpApiOptions settings = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(settings.ApiKey);

    public async Task<HomeDepotLookupResult?> FindAsync(string description, CancellationToken cancellationToken)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(description)) return null;

        var cacheKey = $"serpapi:home-depot:{description.Trim().ToLowerInvariant()}";
        if (cache.TryGetValue(cacheKey, out HomeDepotLookupResult? cached)) return cached;

        var query = $"{description.Trim()} Home Depot";
        var path = "/search.json?engine=google_shopping" +
            $"&q={Uri.EscapeDataString(query)}" +
            $"&location={Uri.EscapeDataString(settings.Location)}" +
            "&hl=en&gl=us" +
            $"&api_key={Uri.EscapeDataString(settings.ApiKey)}";
        using var response = await httpClient.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("SerpApi lookup returned HTTP {StatusCode}.", (int)response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<SearchResponse>(stream, JsonOptions, cancellationToken);
        var candidates = (payload?.ShoppingResults ?? [])
            .Where(item => item.Source.Contains("Home Depot", StringComparison.OrdinalIgnoreCase))
            .Where(item => item.ExtractedPrice is > 0 && !string.IsNullOrWhiteSpace(item.Title))
            .Take(Math.Clamp(settings.MaximumResults, 1, 40))
            .Select(item => Score(description, item))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Item.ExtractedPrice)
            .ToList();
        if (candidates.Count == 0) return null;

        var best = candidates[0];
        var nextScore = candidates.Count > 1 ? candidates[1].Score : 0;
        var isExact = best.Score >= .86m && best.DimensionMatch && best.Score - nextScore >= .08m;
        var isSimilar = !isExact && best.Score >= .45m;
        if (!isExact && !isSimilar) return null;

        var result = new HomeDepotLookupResult(
            isExact ? HomeDepotMatchKinds.Exact : HomeDepotMatchKinds.Similar,
            best.Item.ProductId,
            best.Item.Title.Trim(),
            best.Item.ExtractedPrice!.Value,
            best.Item.Link,
            best.Score,
            query);
        cache.Set(cacheKey, result, TimeSpan.FromHours(12));
        return result;
    }

    private static ScoredResult Score(string requested, ShoppingResult candidate)
    {
        var requestedWords = Words(requested);
        var candidateWords = Words(candidate.Title);
        var common = requestedWords.Intersect(candidateWords).Count();
        var coverage = requestedWords.Count == 0 ? 0 : (decimal)common / requestedWords.Count;
        var precision = candidateWords.Count == 0 ? 0 : (decimal)common / candidateWords.Count;
        var requestedDimensions = Dimensions(requested);
        var candidateDimensions = Dimensions(candidate.Title);
        var dimensionMatch = requestedDimensions.Count == 0 || requestedDimensions.SetEquals(candidateDimensions);
        var dimensionScore = requestedDimensions.Count == 0 ? 0.10m : dimensionMatch ? 0.35m : -0.35m;
        var score = Math.Clamp(coverage * .55m + precision * .20m + dimensionScore, 0, 1);
        return new ScoredResult(candidate, score, dimensionMatch);
    }

    private static HashSet<string> Words(string value) => Regex.Matches(value.ToLowerInvariant(), "[a-z0-9]+")
        .Select(match => match.Value)
        .Where(word => word.Length >= 2 && word is not "the" and not "for" and not "with" and not "home" and not "depot")
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> Dimensions(string value)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = Regex.Matches(
            value.ToLowerInvariant().Replace('×', 'x'),
            @"\b(?<a>\d+(?:\.\d+)?)\s*(?<au>in\.?|inch(?:es)?|ft\.?|feet|['""])?\s*x\s*(?<b>\d+(?:\.\d+)?)\s*(?<bu>in\.?|inch(?:es)?|ft\.?|feet|['""])?(?:\s*x\s*(?<c>\d+(?:\.\d+)?)\s*(?<cu>in\.?|inch(?:es)?|ft\.?|feet|['""])?)?");
        foreach (Match match in matches)
        {
            var a = decimal.Parse(match.Groups["a"].Value, CultureInfo.InvariantCulture);
            var b = decimal.Parse(match.Groups["b"].Value, CultureInfo.InvariantCulture);
            if (!match.Groups["c"].Success)
            {
                results.Add($"{a:0.###}x{b:0.###}");
                continue;
            }
            var c = decimal.Parse(match.Groups["c"].Value, CultureInfo.InvariantCulture);
            var unit = match.Groups["cu"].Value;
            var lengthInches = unit.StartsWith("ft", StringComparison.OrdinalIgnoreCase) || unit == "'" ||
                (string.IsNullOrWhiteSpace(unit) && c <= 30) ? c * 12 : c;
            results.Add($"{a:0.###}x{b:0.###}x{lengthInches:0.###}");
        }
        return results;
    }

    private sealed record ScoredResult(ShoppingResult Item, decimal Score, bool DimensionMatch);
    private sealed class SearchResponse
    {
        [JsonPropertyName("shopping_results")] public List<ShoppingResult> ShoppingResults { get; set; } = [];
    }
    private sealed class ShoppingResult
    {
        [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
        [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
        [JsonPropertyName("extracted_price")] public decimal? ExtractedPrice { get; set; }
        [JsonPropertyName("product_id")] public string? ProductId { get; set; }
        [JsonPropertyName("link")] public string? Link { get; set; }
    }
}

public static class HomeDepotMatchKinds
{
    public const string Exact = "Exact";
    public const string Similar = "Similar";
}

public sealed record HomeDepotLookupResult(
    string MatchKind,
    string? ProductId,
    string Title,
    decimal UnitPrice,
    string? ProductUrl,
    decimal Confidence,
    string SearchQuery);
