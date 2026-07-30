using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class CentComTaskAnalysisService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    CentComChatClient centCom,
    IOptions<CentComOptions> options,
    ILogger<CentComTaskAnalysisService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public async Task ProcessAsync(int jobId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var job = await db.QuoteProcessingJobs
            .Include(item => item.QuoteProjectTask!)
                .ThenInclude(task => task.QuoteCase)
            .SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        if (job?.QuoteProjectTask is null) return;

        var analysis = await db.QuoteTaskAnalyses
            .Include(item => item.Materials)
            .Include(item => item.Exclusions)
            .Where(item => item.QuoteProjectTaskId == job.QuoteProjectTaskId)
            .OrderByDescending(item => item.RevisionNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (analysis is null) return;

        job.Status = "Processing";
        job.Message = "CentCom is generating a material plan.";
        analysis.Status = QuoteTaskAnalysisStatuses.Processing;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            if (!centCom.IsConfigured)
            {
                throw new InvalidOperationException(
                    "CentCom is not configured. Set CentCom__BaseUrl and CentCom__Model.");
            }

            var catalog = await LoadCatalogAsync(db, cancellationToken);
            var exclusionRules = await db.MaterialExclusionRules.AsNoTracking()
                .Where(rule => rule.IsActive && (rule.TaskType == null || rule.TaskType == job.QuoteProjectTask.TaskType))
                .OrderByDescending(rule => rule.MatchPhrase.Length)
                .ToListAsync(cancellationToken);
            var requestMessages = new CentComChatClient.RequestMessage[]
            {
                new("system", BuildSystemPrompt()),
                new("user", BuildTaskPrompt(job.QuoteProjectTask))
            };
            var response = await centCom.CompleteJsonAsync(requestMessages, cancellationToken);
            AnalysisResponse result;
            try
            {
                result = ParseResponse(response);
            }
            catch (JsonException)
            {
                logger.LogWarning(
                    "CentCom returned malformed JSON for job {JobId}; requesting one repair.",
                    jobId);
                response = await centCom.CompleteJsonAsync(
                [
                    .. requestMessages,
                    new("user",
                        "The previous response was invalid or truncated. Regenerate the FULL answer as valid JSON " +
                        "matching the required schema. Keep assumptions, warnings, and material notes concise. " +
                        "Include every required material category. Return JSON only.")
                ], cancellationToken);
                result = ParseResponse(response);
            }

            db.QuoteTaskAnalysisMaterials.RemoveRange(analysis.Materials);
            db.QuoteTaskAnalysisExclusions.RemoveRange(analysis.Exclusions);
            analysis.Materials.Clear();
            analysis.Exclusions.Clear();
            var sortOrder = 1;
            foreach (var proposed in result.Materials)
            {
                var exclusion = exclusionRules.FirstOrDefault(rule =>
                    proposed.Description.Contains(rule.MatchPhrase, StringComparison.OrdinalIgnoreCase));
                if (exclusion is not null)
                {
                    analysis.Exclusions.Add(new QuoteTaskAnalysisExclusion
                    {
                        MaterialExclusionRuleId = exclusion.Id,
                        Description = Trim(proposed.Description, 500) ?? "Unspecified material",
                        Reason = exclusion.Reason
                    });
                    continue;
                }
                var match = FindCatalogMatch(proposed, catalog);
                var quantity = Math.Max(0, proposed.Quantity);
                var wastePercent = proposed.WastePercent is > 0 and <= 1
                    ? proposed.WastePercent * 100
                    : proposed.WastePercent;
                analysis.Materials.Add(new QuoteTaskAnalysisMaterial
                {
                    SortOrder = sortOrder++,
                    VendorProductId = match?.VendorProductId,
                    VendorSku = match?.Sku ?? proposed.VendorSku?.Trim(),
                    Description = Trim(proposed.Description, 500) ?? "Unspecified material",
                    Quantity = quantity,
                    Unit = Trim(proposed.Unit, 40) ?? match?.Unit ?? "Each",
                    UnitCost = match?.UnitPrice ?? 0,
                    WastePercent = Math.Clamp(wastePercent, 0, 100),
                    MatchConfidence = match is null ? 0 : 1,
                    SourceType = match is null ? "CentCom estimate - unmatched" : match.VendorName,
                    SourceReference = match is null
                        ? null
                        : string.Join(" · ", new[] { match.SourceType, match.SourceReference }
                            .Where(value => !string.IsNullOrWhiteSpace(value))),
                    SourcePriceDate = match?.EffectiveDate,
                    IsUnmatched = match is null,
                    Notes = Trim(proposed.Notes, 1000)
                });
            }

            analysis.Status = QuoteTaskAnalysisStatuses.NeedsReview;
            analysis.ModelVersion = Trim(options.Value.Model, 120);
            analysis.Assumptions = Trim(string.Join(Environment.NewLine, result.Assumptions), 4000);
            var validationWarnings = ValidateDeckAnalysis(job.QuoteProjectTask, analysis.Materials, analysis.Exclusions);
            analysis.QuestionsAndWarnings = Trim(
                string.Join(Environment.NewLine, result.Warnings
                    .Concat(validationWarnings)
                    .Concat(analysis.Exclusions.Select(item =>
                    {
                        var rule = exclusionRules.FirstOrDefault(x => x.Id == item.MaterialExclusionRuleId);
                        var recovery = string.IsNullOrWhiteSpace(rule?.RecoveryType)
                            ? "WARNING: no recovery method is mapped"
                            : $"Recovered through {rule.RecoveryType}: {rule.RecoveryReference ?? "reference not supplied"}";
                        return $"[EXCLUDED BY POLICY] {item.Description}: {item.Reason} ({recovery})";
                    }))
                    .Concat(analysis.Materials.Where(item => item.IsUnmatched)
                        .Select(item => $"No active vendor price match: {item.Description}"))),
                4000);
            analysis.DeliveryAllowance = Math.Max(0, result.DeliveryAllowance);
            analysis.TaxAllowance = Math.Max(0, result.TaxAllowance);
            analysis.OtherAllowance = Math.Max(0, result.OtherAllowance);
            analysis.CompletedAt = DateTimeOffset.UtcNow;
            job.Status = "Completed";
            job.Message = $"{analysis.Materials.Count} material line(s) generated; {analysis.Exclusions.Count} suggestion(s) excluded by policy; administrator review required.";
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "CentCom task analysis job {JobId} failed.", jobId);
            analysis.Status = QuoteTaskAnalysisStatuses.Failed;
            analysis.CompletedAt = DateTimeOffset.UtcNow;
            analysis.QuestionsAndWarnings = Trim(exception.Message, 4000);
            job.Status = "Failed";
            job.Message = Trim(exception.Message, 500);
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static async Task<List<CatalogItem>> LoadCatalogAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var products = await db.VendorProducts
            .AsNoTracking()
            .Include(item => item.SupplyVendor)
            .Include(item => item.Prices)
            .Where(item => item.IsActive && item.SupplyVendor.IsActive)
            .ToListAsync(cancellationToken);

        return products.Select(product =>
        {
            var price = product.Prices
                .Where(item => item.EffectiveDate <= DateOnly.FromDateTime(DateTime.UtcNow) &&
                    (item.ExpirationDate == null || item.ExpirationDate >= DateOnly.FromDateTime(DateTime.UtcNow)))
                .OrderByDescending(item => item.EffectiveDate)
                .ThenByDescending(item => item.Id)
                .FirstOrDefault();
            return new CatalogItem(
                product.Id,
                product.SupplyVendor.Name,
                product.VendorSku,
                product.VendorDescription ?? product.Product.Name,
                product.PurchaseUnit,
                price?.UnitPrice ?? 0,
                price?.EffectiveDate,
                price?.SourceType,
                price?.SourceReference);
        }).Where(item => item.UnitPrice > 0)
            .OrderByDescending(item => IsPreferredVendor(item.VendorName))
            .ThenBy(item => item.VendorName)
            .ThenBy(item => item.Sku)
            .ToList();
    }

    private static string BuildSystemPrompt() => """
        You are CentCom, a construction estimating assistant. Return JSON only, without markdown.
        Create a COMPLETE but consolidated proposed supply list for the described project.
        Include framing, decking, connectors, fasteners, footings,
        guards, stair components, flashing/water management, demolition consumables, and disposal
        when applicable. Set vendorSku to null; the application will match catalogs deterministically.
        Combine identical materials and sizes across project levels into one line. Return no more than
        30 material lines, using category allowances such as a dumpster load where appropriate.
        Calculate decking coverage from actual square footage and board face width. Calculate a
        planning joist count from the stated deck dimensions and explicitly state the assumed
        framing direction and spacing.

        This is an estimating aid, not a structural design or permit approval. Explicitly identify
        assumptions and unresolved structural/code questions. Multi-level decks and unusual loads
        require project-specific design and authority-having-jurisdiction approval. Base general
        deck guidance on:
        - AWC DCA 6, Prescriptive Residential Wood Deck Construction Guide (single-level scope):
          https://awc.org/wp-content/uploads/2022/02/AWC-DCA62015-DeckGuide-1804.pdf
        - Trex Deck Building Guides and current product installation instructions:
          https://www.trex.com/academy/how-to-guides/
        Do not claim either source approves a specific design.

        JSON schema:
        {
          "assumptions": ["string"],
          "warnings": ["string"],
          "deliveryAllowance": 0,
          "taxAllowance": 0,
          "otherAllowance": 0,
          "materials": [
            {
              "vendorSku": "string or null",
              "description": "specific material and dimensions",
              "quantity": 0,
              "unit": "Each|Piece|Box|Bag|Linear Foot|Square Foot|Cubic Yard|Load",
              "wastePercent": 0,
              "notes": "calculation basis or required verification"
            }
          ]
        }
        Keep all descriptive strings concise so the complete JSON response fits within the model output limit.
        """;

    private static string BuildTaskPrompt(QuoteProjectTask task)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"TASK TYPE: {task.TaskType}");
        builder.AppendLine("PROJECT OVERVIEW:");
        builder.AppendLine(task.QuoteCase.WorkDescription);
        builder.AppendLine("TASK SCOPE:");
        builder.AppendLine(task.ScopeOfWork);
        builder.AppendLine();
        builder.AppendLine(
            "Generate conservative planning quantities, consolidate identical products, keep notes short, " +
            "and make every missing measurement or design choice explicit.");
        return builder.ToString();
    }

    private static AnalysisResponse ParseResponse(string response)
    {
        var json = response.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = json.IndexOf('\n');
            var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
                json = json[(firstNewLine + 1)..lastFence].Trim();
        }

        var result = JsonSerializer.Deserialize<AnalysisResponse>(json, JsonOptions);
        if (result is null || result.Materials.Count == 0)
            throw new InvalidOperationException("CentCom returned no material recommendations.");
        return result;
    }

    private static string? Trim(string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maximum)];

    private static CatalogItem? FindCatalogMatch(
        MaterialResponse proposed,
        IReadOnlyList<CatalogItem> catalog)
    {
        if (!string.IsNullOrWhiteSpace(proposed.VendorSku))
        {
            var exactSkuMatches = catalog
                .Where(item => item.Sku.Equals(proposed.VendorSku.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => IsPreferredVendor(item.VendorName))
                .ThenBy(item => item.UnitPrice)
                .ToList();
            if (exactSkuMatches.Count > 0) return exactSkuMatches[0];
        }

        var proposedDimensions = DimensionTokens(proposed.Description);
        var proposedWords = WordTokens(proposed.Description);
        var candidates = catalog.Select(item =>
        {
            var description = $"{item.Description} {item.Sku}";
            var itemDimensions = DimensionTokens(description);
            var dimensionScore = proposedDimensions.Count == 0
                ? 0
                : proposedDimensions.Intersect(itemDimensions).Count() * 8;
            var words = WordTokens(description);
            var wordScore = proposedWords.Intersect(words).Count();
            var preferredBonus = IsPreferredVendor(item.VendorName) ? 0.25 : 0;
            return new { Item = item, Score = dimensionScore + wordScore + preferredBonus };
        })
        .Where(candidate => candidate.Score >= (proposedDimensions.Count > 0 ? 10 : 5))
        .OrderByDescending(candidate => candidate.Score)
        .ThenByDescending(candidate => IsPreferredVendor(candidate.Item.VendorName))
        .ThenBy(candidate => candidate.Item.UnitPrice)
        .ToList();

        if (candidates.Count == 0) return null;
        if (candidates.Count > 1 && candidates[0].Score - candidates[1].Score < 1 &&
            !candidates[0].Item.Description.Equals(candidates[1].Item.Description, StringComparison.OrdinalIgnoreCase))
            return null;
        return candidates[0].Item;
    }

    private static List<string> ValidateDeckAnalysis(
        QuoteProjectTask task,
        ICollection<QuoteTaskAnalysisMaterial> materials,
        ICollection<QuoteTaskAnalysisExclusion> exclusions)
    {
        if (!task.TaskType.Equals("Deck", StringComparison.OrdinalIgnoreCase)) return [];

        var warnings = new List<string>();
        var combinedScope = $"{task.QuoteCase.WorkDescription} {task.ScopeOfWork}";
        var deckDimensions = Regex.Matches(
                combinedScope,
                @"(?<length>\d+(?:\.\d+)?)\s*[’']?\s*[x×]\s*(?<width>\d+(?:\.\d+)?)",
                RegexOptions.IgnoreCase)
            .Select(match => (
                Length: decimal.Parse(match.Groups["length"].Value, CultureInfo.InvariantCulture),
                Width: decimal.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture)))
            .Where(size => size.Length >= 4 && size.Width >= 4)
            .Take(4)
            .ToList();

        if (deckDimensions.Count > 0)
        {
            var totalArea = deckDimensions.Sum(size => size.Length * size.Width);
            var minimumDeckingLinearFeet = decimal.Ceiling(totalArea * 12m / 5.5m);
            var proposedDeckingLinearFeet = materials
                .Where(item => ContainsAny(item.Description, "decking", "deck board"))
                .Sum(item => item.Unit.Contains("linear", StringComparison.OrdinalIgnoreCase)
                    ? item.Quantity
                    : item.Quantity * 12);
            if (proposedDeckingLinearFeet < minimumDeckingLinearFeet * 0.9m)
            {
                warnings.Add(
                    $"[QUANTITY CHECK] Proposed decking ({proposedDeckingLinearFeet:0} linear ft) is below the approximate " +
                    $"{minimumDeckingLinearFeet:0} linear ft needed to cover {totalArea:0} sq ft with nominal 1x6 boards before waste.");
            }

            var minimumJoists = deckDimensions.Sum(size =>
                decimal.Ceiling(size.Length * 12m / 16m) + 1);
            var proposedJoists = materials
                .Where(item => ContainsAny(item.Description, "joist"))
                .Sum(item => item.Quantity);
            if (proposedJoists < minimumJoists)
            {
                warnings.Add(
                    $"[QUANTITY CHECK] Proposed joists ({proposedJoists:0}) are below the planning check of " +
                    $"{minimumJoists:0} at 16-inch spacing across the listed deck widths. Confirm framing direction and engineered design.");
            }
        }

        var requiredCategories = new (string Name, string[] Terms)[]
        {
            ("decking", ["decking", "deck board"]),
            ("joists", ["joist"]),
            ("beams or headers", ["beam", "header"]),
            ("posts", ["post"]),
            ("ledger and flashing", ["ledger", "flashing"]),
            ("footings or concrete", ["footing", "concrete"]),
            ("connectors or hangers", ["connector", "hanger", "bracket"]),
            ("fasteners", ["fastener", "screw", "nail", "bolt"]),
            ("guards or railings", ["guard", "rail", "baluster"]),
            ("stairs", ["stair", "stringer", "tread"]),
            ("demolition and disposal", ["demolition", "disposal", "dumpster", "debris"])
        };
        foreach (var category in requiredCategories.Where(category =>
                     !materials.Any(item => ContainsAny(item.Description, category.Terms)) &&
                     !exclusions.Any(item => ContainsAny(item.Description, category.Terms))))
        {
            warnings.Add($"[COVERAGE CHECK] The proposed supply list is missing {category.Name}.");
        }

        return warnings;
    }

    private static HashSet<string> DimensionTokens(string value) =>
        Regex.Matches(
                value.ToLowerInvariant().Replace('×', 'x'),
                @"\b\d+(?:\.\d+)?\s*x\s*\d+(?:\.\d+)?(?:\s*x\s*\d+(?:\.\d+)?)?\b")
            .Select(match => Regex.Replace(match.Value, @"\s+", ""))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> WordTokens(string value) =>
        Regex.Matches(value.ToLowerInvariant(), @"[a-z0-9]+")
            .Select(match => match.Value)
            .Where(word => word.Length >= 3 && word is not "the" and not "for" and not "with" and not "each")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool IsPreferredVendor(string vendorName) =>
        vendorName.Equals("Decks & Docks", StringComparison.OrdinalIgnoreCase) ||
        vendorName.Equals("Deck & Docks", StringComparison.OrdinalIgnoreCase);

    private sealed record CatalogItem(
        int VendorProductId,
        string VendorName,
        string Sku,
        string Description,
        string Unit,
        decimal UnitPrice,
        DateOnly? EffectiveDate,
        string? SourceType,
        string? SourceReference);

    private sealed class AnalysisResponse
    {
        public List<string> Assumptions { get; set; } = [];
        public List<string> Warnings { get; set; } = [];
        public decimal DeliveryAllowance { get; set; }
        public decimal TaxAllowance { get; set; }
        public decimal OtherAllowance { get; set; }
        public List<MaterialResponse> Materials { get; set; } = [];
    }

    private sealed class MaterialResponse
    {
        public string? VendorSku { get; set; }
        public string Description { get; set; } = "";
        public decimal Quantity { get; set; }
        public string Unit { get; set; } = "Each";
        public decimal WastePercent { get; set; }
        public string? Notes { get; set; }
    }
}
