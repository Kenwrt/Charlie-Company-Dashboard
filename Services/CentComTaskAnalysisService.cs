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
    HomeDepotCatalogLookupService homeDepot,
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
            .Include(item => item.ReviewItems)
            .Where(item => item.QuoteProjectTaskId == job.QuoteProjectTaskId)
            .OrderByDescending(item => item.RevisionNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (analysis is null)
        {
            var revision = (await db.QuoteTaskAnalyses
                .Where(item => item.QuoteProjectTaskId == job.QuoteProjectTaskId)
                .Select(item => (int?)item.RevisionNumber)
                .MaxAsync(cancellationToken) ?? 0) + 1;
            analysis = new QuoteTaskAnalysis
            {
                QuoteProjectTaskId = job.QuoteProjectTask.Id,
                RevisionNumber = revision,
                Status = QuoteTaskAnalysisStatuses.Processing
            };
            db.QuoteTaskAnalyses.Add(analysis);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Created missing CentCom analysis revision {Revision} while recovering queued job {JobId}.",
                revision,
                jobId);
        }
        var priorAnalysis = await db.QuoteTaskAnalyses.AsNoTracking()
            .Include(item => item.Materials)
            .Include(item => item.ReviewItems)
            .Where(item => item.QuoteProjectTaskId == job.QuoteProjectTaskId && item.RevisionNumber < analysis.RevisionNumber)
            .OrderByDescending(item => item.RevisionNumber)
            .FirstOrDefaultAsync(cancellationToken);
        var resolvedReviewHistory = await db.QuoteTaskAnalysisReviewItems.AsNoTracking()
            .Include(item => item.QuoteTaskAnalysis)
            .Where(item => item.QuoteTaskAnalysis.QuoteProjectTaskId == job.QuoteProjectTaskId
                && item.QuoteTaskAnalysis.RevisionNumber < analysis.RevisionNumber
                && item.Status != AnalysisReviewStatuses.NeedsReview
                && item.Status != AnalysisReviewStatuses.FieldVerification)
            .OrderBy(item => item.QuoteTaskAnalysis.RevisionNumber)
            .ThenBy(item => item.SortOrder)
            .ToListAsync(cancellationToken);

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
            var reusableRules = await db.CentComResolutionRules.AsNoTracking()
                .Where(rule => rule.IsActive && rule.TaskType == job.QuoteProjectTask.TaskType)
                .OrderByDescending(rule => rule.CreatedAt)
                .ToListAsync(cancellationToken);
            var requestMessages = new CentComChatClient.RequestMessage[]
            {
                new("system", BuildSystemPrompt()),
                new("user", BuildTaskPrompt(job.QuoteProjectTask, resolvedReviewHistory, reusableRules))
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
                try
                {
                    result = ParseResponse(response);
                }
                catch (JsonException repairException)
                {
                    logger.LogWarning(
                        repairException,
                        "CentCom JSON repair was still malformed for job {JobId}; deriving a safe material plan from the saved task scope.",
                        jobId);
                    result = BuildScopeFallback(job.QuoteProjectTask);
                }
            }

            if (result.Materials.Count == 0)
            {
                logger.LogWarning(
                    "CentCom returned no materials for job {JobId}; requesting one complete material-plan repair.",
                    jobId);
                response = await centCom.CompleteJsonAsync(
                [
                    .. requestMessages,
                    new("user",
                        "The previous answer contained no material line items. Regenerate the FULL JSON answer with a practical " +
                        "material plan derived from the task scope. Treat brand names and general product descriptions as search " +
                        "intents; do not require an exact catalog description. Include requested decking and railing products when " +
                        "they appear in the scope, use the supplied dimensions for planning quantities, and identify assumptions. " +
                        "Return JSON only.")
                ], cancellationToken);
                result = ParseResponse(response);
            }

            if (result.Materials.Count == 0)
            {
                logger.LogWarning(
                    "CentCom repair still returned no materials for job {JobId}; deriving safe search intents from the saved task scope.",
                    jobId);
                result = BuildScopeFallback(job.QuoteProjectTask);
            }

            NormalizeDeckMaterialPlan(job.QuoteProjectTask, result);

            db.QuoteTaskAnalysisMaterials.RemoveRange(analysis.Materials);
            db.QuoteTaskAnalysisExclusions.RemoveRange(analysis.Exclusions);
            db.QuoteTaskAnalysisReviewItems.RemoveRange(analysis.ReviewItems);
            analysis.Materials.Clear();
            analysis.Exclusions.Clear();
            analysis.ReviewItems.Clear();
            // Persist removals before reusing revision-scoped item keys such as r01.
            // PostgreSQL can otherwise attempt an insert before the matching delete
            // and reject the batch on the unique analysis/item-key constraint.
            await db.SaveChangesAsync(cancellationToken);
            var sortOrder = 1;
            foreach (var proposed in result.Materials)
            {
                var materialRule = FindReusableRule(reusableRules, CentComResolutionRuleKinds.Material, proposed.Description);
                if (materialRule?.MaterialDecision == MaterialReviewDecisions.Removed)
                {
                    continue;
                }
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
                var match = materialRule?.VendorProductId is int preferredProductId
                    ? catalog.FirstOrDefault(item => item.VendorProductId == preferredProductId)
                    : FindCatalogMatch(proposed, catalog);
                HomeDepotLookupResult? remoteMatch = null;
                if (match is null && materialRule is null)
                {
                    try
                    {
                        remoteMatch = await homeDepot.FindAsync(proposed.Description, cancellationToken);
                    }
                    catch (Exception lookupException) when (!cancellationToken.IsCancellationRequested)
                    {
                        logger.LogWarning(
                            lookupException,
                            "Home Depot lookup failed for '{Description}' in CentCom job {JobId}; preserving the material as unresolved for estimator review.",
                            Trim(proposed.Description, 160),
                            jobId);
                    }
                    if (remoteMatch?.MatchKind == HomeDepotMatchKinds.Exact)
                    {
                        match = await GetOrCreateHomeDepotCatalogItemAsync(db, remoteMatch, proposed.Unit, cancellationToken);
                        catalog.Add(match);
                    }
                }
                var (quantity, resolvedUnit) = ResolvePurchaseQuantity(proposed, match);
                var wastePercent = proposed.WastePercent is > 0 and <= 1
                    ? proposed.WastePercent * 100
                    : proposed.WastePercent;
                analysis.Materials.Add(new QuoteTaskAnalysisMaterial
                {
                    SortOrder = sortOrder++,
                    VendorProductId = match?.VendorProductId,
                    VendorSku = match?.Sku ?? (remoteMatch?.MatchKind == HomeDepotMatchKinds.Similar ? RemoteSku(remoteMatch) : proposed.VendorSku?.Trim()),
                    Description = match is not null
                        ? match.Description
                        : remoteMatch?.MatchKind == HomeDepotMatchKinds.Similar
                        ? Trim(remoteMatch.Title, 500) ?? "Unspecified Home Depot alternative"
                        : Trim(proposed.Description, 500) ?? "Unspecified material",
                    OriginalDescription = Trim(proposed.Description, 500) ?? "Unspecified material",
                    Quantity = quantity,
                    Unit = resolvedUnit,
                    UnitCost = match?.UnitPrice ?? remoteMatch?.UnitPrice ?? materialRule?.MaterialUnitCost ?? 0,
                    WastePercent = Math.Clamp(wastePercent, 0, 100),
                    MatchConfidence = match is not null || materialRule is not null ? 1 : remoteMatch?.Confidence ?? 0,
                    SourceType = match is not null ? match.VendorName : remoteMatch is not null ? "Home Depot via SerpApi" : materialRule is not null ? "Reusable estimator rule" : "CentCom estimate - unmatched",
                    SourceReference = match is null
                        ? Trim(remoteMatch?.ProductUrl ?? remoteMatch?.SearchQuery, 255)
                        : string.Join(" · ", new[] { match.SourceType, match.SourceReference }
                            .Where(value => !string.IsNullOrWhiteSpace(value))),
                    SourcePriceDate = match?.EffectiveDate,
                    IsUnmatched = match is null && remoteMatch is null && materialRule is null,
                    MatchKind = match is not null
                        ? (match.VendorName.Equals("Home Depot", StringComparison.OrdinalIgnoreCase) ? MaterialMatchKinds.HomeDepotExact : MaterialMatchKinds.Catalog)
                        : remoteMatch is not null ? MaterialMatchKinds.HomeDepotSimilar : materialRule is not null ? MaterialMatchKinds.OneOff : MaterialMatchKinds.Unresolved,
                    ReviewDecision = match is not null || materialRule is not null ? MaterialReviewDecisions.Accepted : MaterialReviewDecisions.Pending,
                    Notes = Trim(proposed.Notes, 1000)
                });
            }

            if (priorAnalysis is not null)
            {
                foreach (var locked in priorAnalysis.Materials.Where(item => item.IsEstimatorLocked && !item.IsRemoved && item.VendorProductId is not null))
                {
                    if (analysis.Materials.Any(item => item.VendorProductId == locked.VendorProductId)) continue;
                    analysis.Materials.Add(new QuoteTaskAnalysisMaterial
                    {
                        SortOrder = sortOrder++, VendorProductId = locked.VendorProductId, VendorSku = locked.VendorSku,
                        Description = locked.Description, OriginalDescription = locked.OriginalDescription,
                        Quantity = locked.Quantity, Unit = locked.Unit, UnitCost = locked.UnitCost,
                        WastePercent = locked.WastePercent, MatchConfidence = 1, SourceType = locked.SourceType,
                        SourceReference = locked.SourceReference, SourcePriceDate = locked.SourcePriceDate,
                        MatchKind = MaterialMatchKinds.Catalog, ReviewDecision = MaterialReviewDecisions.Accepted,
                        IsEstimatorLocked = true,
                        Notes = "Carried forward from an estimator-locked decision in the prior revision."
                    });
                }
            }

            analysis.Status = QuoteTaskAnalysisStatuses.NeedsReview;
            analysis.ModelVersion = Trim(options.Value.Model, 120);
            var generatedAssumptionItems = result.Assumptions
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => (Label: "Assumption", Body: item.Trim()))
                .Where(item => !WasPreviouslyResolved(item, AnalysisReviewKinds.Assumption, resolvedReviewHistory))
                .Where(item => FindReusableRule(reusableRules, CentComResolutionRuleKinds.Assumption, item.Body) is null)
                .ToList();
            analysis.Assumptions = Trim(string.Join(Environment.NewLine, generatedAssumptionItems.Select(item => item.Body)), 4000);
            var validationWarnings = ValidateDeckAnalysis(job.QuoteProjectTask, analysis.Materials, analysis.Exclusions);
            var generatedReviewItems = ParseReviewItems(
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
                    .Concat(analysis.Materials.Where(item => item.MatchKind == MaterialMatchKinds.HomeDepotSimilar)
                        .Select(item => $"[SIMILAR PRODUCT] Requested '{item.OriginalDescription}'; proposed '{item.Description}'. Review before accepting."))
                    .Concat(analysis.Materials.Where(item => item.IsUnmatched)
                        .Select(item => $"[UNRESOLVED MATERIAL] {item.OriginalDescription ?? item.Description}: enter a catalog item or one-off price."))))
                .Where(item => !WasPreviouslyResolved(item, AnalysisReviewKinds.Warning, resolvedReviewHistory))
                .Where(item => FindReusableRule(reusableRules, CentComResolutionRuleKinds.Warning, item.Body) is null)
                .ToList();
            analysis.QuestionsAndWarnings = Trim(
                string.Join(Environment.NewLine, generatedReviewItems.Select(item => $"[{item.Label.ToUpperInvariant()}] {item.Body}")),
                4000);
            var reviewNumber = 1;
            foreach (var assumption in generatedAssumptionItems)
            {
                analysis.ReviewItems.Add(new QuoteTaskAnalysisReviewItem
                {
                    SortOrder = reviewNumber,
                    ItemKey = $"a{reviewNumber++:D2}",
                    ReviewKind = AnalysisReviewKinds.Assumption,
                    Category = assumption.Label,
                    Description = assumption.Body
                });
            }
            reviewNumber = 1;
            foreach (var review in generatedReviewItems)
            {
                analysis.ReviewItems.Add(new QuoteTaskAnalysisReviewItem
                {
                    SortOrder = reviewNumber,
                    ItemKey = $"r{reviewNumber++:D2}",
                    ReviewKind = AnalysisReviewKinds.Warning,
                    Category = review.Label,
                    Description = review.Body
                });
            }
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

    private static async Task<CatalogItem> GetOrCreateHomeDepotCatalogItemAsync(
        ApplicationDbContext db,
        HomeDepotLookupResult result,
        string proposedUnit,
        CancellationToken cancellationToken)
    {
        var vendor = await db.SupplyVendors.SingleOrDefaultAsync(
            item => item.Name.ToLower() == "home depot", cancellationToken);
        if (vendor is null)
        {
            vendor = new SupplyVendor { Name = "Home Depot", LegalName = "The Home Depot", IsActive = true };
            db.SupplyVendors.Add(vendor);
            await db.SaveChangesAsync(cancellationToken);
        }

        var sku = RemoteSku(result);
        var item = await db.VendorProducts
            .Include(product => product.Prices)
            .Include(product => product.Product)
            .SingleOrDefaultAsync(product => product.SupplyVendorId == vendor.Id && product.VendorSku == sku, cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (item is null)
        {
            var unit = Trim(proposedUnit, 40) ?? "Each";
            item = new VendorProduct
            {
                SupplyVendorId = vendor.Id,
                VendorSku = sku,
                VendorDescription = Trim(result.Title, 300),
                PurchaseUnit = unit,
                PackageQuantity = 1,
                IsActive = true,
                Product = new Product
                {
                    Name = Trim(result.Title, 160) ?? "Home Depot product",
                    Category = CategoryForRemoteProduct(result.Title),
                    Manufacturer = "Home Depot",
                    ManufacturerPartNumber = result.ProductId,
                    UnitOfMeasure = unit,
                    IsActive = true
                }
            };
            db.VendorProducts.Add(item);
        }

        var price = item.Prices.SingleOrDefault(value => value.EffectiveDate == today);
        if (price is null)
        {
            var current = item.Prices.Where(value => value.ExpirationDate is null).OrderByDescending(value => value.EffectiveDate).FirstOrDefault();
            if (current is not null && current.EffectiveDate < today) current.ExpirationDate = today.AddDays(-1);
            price = new VendorPrice
            {
                UnitPrice = result.UnitPrice,
                EffectiveDate = today,
                SourceType = "SerpApi exact match",
                SourceReference = Trim(result.ProductUrl ?? result.SearchQuery, 200)
            };
            item.Prices.Add(price);
        }
        else if (price.UnitPrice != result.UnitPrice)
        {
            price.UnitPrice = result.UnitPrice;
            price.SourceReference = Trim(result.ProductUrl ?? result.SearchQuery, 200);
        }
        await db.SaveChangesAsync(cancellationToken);
        return new CatalogItem(item.Id, vendor.Name, sku, item.VendorDescription ?? item.Product.Name,
            item.Product.Category, item.ProductSystem, item.IsPreferred, item.PreferencePriority,
            item.PurchaseUnit, result.UnitPrice, today, price.SourceType, price.SourceReference);
    }

    private static string RemoteSku(HomeDepotLookupResult result) =>
        !string.IsNullOrWhiteSpace(result.ProductId)
            ? $"HD-{Trim(result.ProductId, 96)}"
            : $"HD-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(result.Title)))[..16]}";

    private static string CategoryForRemoteProduct(string description) =>
        ContainsAny(description, "deck", "board") ? "Decking and Accessories" :
        ContainsAny(description, "screw", "nail", "bolt", "fastener") ? "Fasteners" :
        ContainsAny(description, "hanger", "bracket", "anchor", "post base") ? "Connectors" :
        "Lumber and Building Materials";

    private static async Task<List<CatalogItem>> LoadCatalogAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var products = await db.VendorProducts
            .AsNoTracking()
            .Include(item => item.SupplyVendor)
            .Include(item => item.Product)
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
                product.Product.Category,
                product.ProductSystem,
                product.IsPreferred,
                product.PreferencePriority,
                product.PurchaseUnit,
                price?.UnitPrice ?? 0,
                price?.EffectiveDate,
                price?.SourceType,
                price?.SourceReference);
        }).Where(item => item.UnitPrice > 0)
            .OrderByDescending(item => item.IsPreferred)
            .ThenBy(item => item.PreferencePriority)
            .ThenByDescending(item => IsPreferredVendor(item.VendorName))
            .ThenBy(item => item.VendorName)
            .ThenBy(item => item.Sku)
            .ToList();
    }

    private static string BuildSystemPrompt() => """
        You are CentCom, a construction estimating assistant. Return JSON only, without markdown.
        Create a COMPLETE but consolidated proposed supply list for the described project.
        Interpret likely speech-to-text substitutions in construction context (for example, "truck" may mean
        "Trex" when describing composite decking or railing). When a brand or product family is requested,
        preserve it in each compatible material description. Specify searchable component categories and include
        every accessory required for a complete install rather than using vague allowances.
        For a railing system, return separate searchable lines for level and stair panels or rails,
        level and stair posts, caps/skirts, brackets, balusters or infill, and manufacturer-required
        fasteners as applicable. Do not collapse a complete railing system into one generic line.
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

    private static string BuildTaskPrompt(
        QuoteProjectTask task,
        IReadOnlyCollection<QuoteTaskAnalysisReviewItem> resolvedReviewHistory,
        IReadOnlyCollection<CentComResolutionRule> reusableRules)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"TASK TYPE: {task.TaskType}");
        builder.AppendLine("PROJECT OVERVIEW:");
        builder.AppendLine(task.QuoteCase.WorkDescription);
        builder.AppendLine("TASK SCOPE:");
        builder.AppendLine(task.ScopeOfWork);
        if (resolvedReviewHistory.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("ESTIMATOR RESOLUTIONS FROM THE PRIOR REVISION (authoritative):");
            foreach (var item in resolvedReviewHistory)
                builder.AppendLine($"- {item.ReviewKind} / {item.Category}: {item.Description} | Disposition: {item.Status} | Response: {item.EstimatorResponse ?? "No note"} | Action: {item.ResolutionAction ?? "No added cost"}");
            builder.AppendLine("Do not repeat any accepted, resolved, or not-applicable item as a question or warning in this or future revisions. Treat the estimator disposition and response as authoritative. Preserve estimator-locked catalog products.");
        }
        var reviewRules = reusableRules.Where(rule => rule.RuleKind != CentComResolutionRuleKinds.Material).ToList();
        if (reviewRules.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"REUSABLE ESTIMATOR RULES FOR {task.TaskType.ToUpperInvariant()} TASKS (authoritative):");
            foreach (var rule in reviewRules)
                builder.AppendLine($"- {rule.RuleKind}: {rule.MatchText} | Disposition: {rule.ReviewStatus} | Response: {rule.EstimatorResponse ?? "No note"} | Action: {rule.ResolutionAction ?? "No added cost"}");
            builder.AppendLine("Apply these rules whenever the same or materially equivalent condition appears. Do not return a resolved rule as an open assumption, question, or warning.");
        }
        builder.AppendLine();
        builder.AppendLine(
            "Generate conservative planning quantities, consolidate identical products, keep notes short, " +
            "and make every missing measurement or design choice explicit.");
        return builder.ToString();
    }

    private static IReadOnlyList<(string Label, string Body)> ParseReviewItems(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return [];
        var separated = Regex.Replace(content.Trim(), @"(?<!^)(?=\[[^\]\r\n]+\])", Environment.NewLine);
        var lines = separated.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Select((line, index) =>
        {
            var marker = Regex.Match(line, @"^\[(?<label>[^\]]+)\]\s*(?<body>.*)$");
            var label = marker.Success
                ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(marker.Groups["label"].Value.Replace('_', ' ').Trim().ToLowerInvariant())
                : $"Review item {index + 1}";
            return (Label: label, Body: marker.Success ? marker.Groups["body"].Value.Trim() : line.Trim());
        }).Where(item => !string.IsNullOrWhiteSpace(item.Body)).ToList();
    }

    private static bool WasPreviouslyResolved(
        (string Label, string Body) candidate,
        string reviewKind,
        IEnumerable<QuoteTaskAnalysisReviewItem> priorItems)
    {
        var candidateLabel = NormalizeReviewText(candidate.Label);
        var candidateBody = NormalizeReviewText(candidate.Body);
        return priorItems
            .Where(item => item.Status != AnalysisReviewStatuses.NeedsReview)
            .Where(item => item.Status != AnalysisReviewStatuses.FieldVerification && item.ReviewKind == reviewKind)
            .Any(item =>
            {
                var priorLabel = NormalizeReviewText(item.Category);
                var priorBody = NormalizeReviewText(item.Description);
                if (candidateBody == priorBody) return true;
                if (candidateLabel != priorLabel) return false;
                return candidateBody.Contains(priorBody, StringComparison.Ordinal)
                    || priorBody.Contains(candidateBody, StringComparison.Ordinal);
            });
    }

    private static CentComResolutionRule? FindReusableRule(
        IEnumerable<CentComResolutionRule> rules,
        string ruleKind,
        string candidate)
    {
        var normalizedCandidate = NormalizeReviewText(candidate);
        return rules.Where(rule => rule.RuleKind == ruleKind)
            .FirstOrDefault(rule =>
            {
                var normalizedRule = NormalizeReviewText(rule.MatchText);
                return normalizedCandidate == normalizedRule
                    || normalizedCandidate.Contains(normalizedRule, StringComparison.Ordinal)
                    || normalizedRule.Contains(normalizedCandidate, StringComparison.Ordinal);
            });
    }

    private static string NormalizeReviewText(string? value) =>
        Regex.Replace(value?.Trim().ToLowerInvariant() ?? string.Empty, @"[^a-z0-9]+", " ").Trim();

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

        var result = JsonSerializer.Deserialize<AnalysisResponse>(json, JsonOptions)
            ?? throw new JsonException("CentCom returned an empty JSON response.");
        result.Materials ??= [];
        result.Assumptions ??= [];
        result.Warnings ??= [];
        return result;
    }

    private static AnalysisResponse BuildScopeFallback(QuoteProjectTask task)
    {
        var scope = $"{task.QuoteCase.WorkDescription} {task.ScopeOfWork}";
        var result = new AnalysisResponse
        {
            Assumptions = ["Material search intents were derived from the saved task scope because CentCom returned no line items."],
            Warnings =
            [
                "[FALLBACK MATERIAL PLAN] Review all quantities and any closest-match substitutions before accepting the estimate."
            ]
        };

        var dimensions = Regex.Match(
            scope,
            @"(?<length>\d+(?:\.\d+)?)\s*(?:[’']?\s*[x×]|\bby\b)\s*(?<width>\d+(?:\.\d+)?)",
            RegexOptions.IgnoreCase);
        decimal? length = null;
        decimal? width = null;
        if (dimensions.Success &&
            decimal.TryParse(dimensions.Groups["length"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedLength) &&
            decimal.TryParse(dimensions.Groups["width"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedWidth))
        {
            length = parsedLength;
            width = parsedWidth;
        }

        if (ContainsAny(scope, "decking", "deck board", "trex"))
        {
            var requestedProduct = scope.Contains("trex", StringComparison.OrdinalIgnoreCase)
                ? "Trex Enhance composite decking"
                : "Decking material";
            result.Materials.Add(new MaterialResponse
            {
                Description = requestedProduct,
                Quantity = length.HasValue && width.HasValue ? length.Value * width.Value : 1,
                Unit = length.HasValue && width.HasValue ? "Square Foot" : "Each",
                WastePercent = 10,
                Notes = length.HasValue && width.HasValue
                    ? $"Planning coverage from {length:0.##} by {width:0.##} ft dimensions; verify board profile, color, and layout."
                    : "Quantity requires field verification; use this description to locate the closest catalog product."
            });
        }

        if (ContainsAny(scope, "railing", "rail", "guard"))
        {
            result.Materials.Add(new MaterialResponse
            {
                Description = scope.Contains("trex", StringComparison.OrdinalIgnoreCase)
                    ? "Trex railing system"
                    : "Deck railing system",
                Quantity = length.HasValue && width.HasValue ? 2 * (length.Value + width.Value) : 1,
                Unit = length.HasValue && width.HasValue ? "Linear Foot" : "Each",
                Notes = length.HasValue && width.HasValue
                    ? "Uses the full perimeter only as a planning allowance; verify house attachment, stairs, openings, posts, and actual guarded edges."
                    : "Quantity and required components must be verified in the field."
            });
        }

        if (result.Materials.Count == 0)
        {
            result.Materials.Add(new MaterialResponse
            {
                Description = Trim(task.ScopeOfWork, 500) ?? $"Materials for {task.TaskType} task",
                Quantity = 1,
                Unit = "Each",
                Notes = "Unresolved scope item retained for catalog search or manual estimator pricing."
            });
        }

        return result;
    }

    private static void NormalizeDeckMaterialPlan(QuoteProjectTask task, AnalysisResponse result)
    {
        if (!task.TaskType.Equals("Deck", StringComparison.OrdinalIgnoreCase)) return;
        var scope = $"{task.QuoteCase.WorkDescription} {task.ScopeOfWork}";
        var dimensions = Regex.Match(
            scope,
            @"(?<length>\d+(?:\.\d+)?)\s*(?:ft\.?\s*)?(?:[’']?\s*[x×]|\bby\b)\s*(?<width>\d+(?:\.\d+)?)",
            RegexOptions.IgnoreCase);
        if (!dimensions.Success ||
            !decimal.TryParse(dimensions.Groups["length"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var length) ||
            !decimal.TryParse(dimensions.Groups["width"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var width)) return;

        var area = length * width;
        var deckingLinearFeet = decimal.Ceiling(area * 12m / 5.5m);
        foreach (var framing in result.Materials.Where(item => MaterialCategory(item.Description) == "Framing" &&
                     item.Description.Contains("trex", StringComparison.OrdinalIgnoreCase)))
        {
            framing.Description = Regex.Replace(framing.Description, @"\bTrex(?:®)?\b", "Pressure-treated", RegexOptions.IgnoreCase);
            framing.Notes = $"{framing.Notes} Trex applies to the finish system; structural framing is resolved separately.".Trim();
        }
        foreach (var decking in result.Materials.Where(item => MaterialCategory(item.Description) == "Decking"))
        {
            decking.Quantity = deckingLinearFeet;
            decking.Unit = "Linear Foot";
            decking.WastePercent = Math.Max(decking.WastePercent, 10);
            decking.Notes = $"{decking.Notes} Calculated from {area:0.##} sq ft using a 5.5-inch board face; available catalog board length will determine piece count.".Trim();
        }

        var railingRequested = ContainsAny(scope, "railing", "rail", "guard");
        if (!railingRequested) return;
        var hasDetailedRailing = result.Materials.Any(item =>
            ContainsAny(item.Description, "railing panel", "rail panel", "railing post", "rail post", "railing bracket", "rail bracket"));
        if (hasDetailedRailing) return;

        result.Materials.RemoveAll(item => MaterialCategory(item.Description) == "Railing");
        var perimeter = 2 * (length + width);
        var panels = decimal.Ceiling(perimeter / 8m);
        var posts = panels + 1;
        result.Materials.AddRange(
        [
            new MaterialResponse { Description = "Trex Enhance Steel 8 ft horizontal railing panel", Quantity = panels, Unit = "Each", WastePercent = 5, Notes = $"Full {perimeter:0.##} ft perimeter planning allowance; verify actual guarded edges and stair openings." },
            new MaterialResponse { Description = "Trex Enhance Steel 37 inch horizontal railing post", Quantity = posts, Unit = "Each", WastePercent = 5, Notes = "Planning count is one more than the level panel count; verify corners, ends, and transitions." },
            new MaterialResponse { Description = "Trex Enhance Steel fixed horizontal railing bracket 4 pack", Quantity = decimal.Ceiling(panels / 2m), Unit = "Each", WastePercent = 5, Notes = "Assumes two brackets per panel and four brackets per package; verify manufacturer instructions." },
            new MaterialResponse { Description = "Trex Enhance Steel railing post cap and skirt", Quantity = posts, Unit = "Each", WastePercent = 5, Notes = "One cap and skirt set per planned post." }
        ]);
        result.Assumptions.Add($"Railing was planned around the full {perimeter:0.##} ft deck perimeter because guarded-edge measurements were not supplied.");
        result.Warnings.Add("[RAILING ASSUMPTION] Confirm which deck edges require railing and whether stairs are present before accepting quantities.");
    }

    private static (decimal Quantity, string Unit) ResolvePurchaseQuantity(MaterialResponse proposed, CatalogItem? match)
    {
        var requestedQuantity = Math.Max(0, proposed.Quantity);
        var requestedUnit = Trim(proposed.Unit, 40) ?? "Each";
        if (match is null) return (requestedQuantity, requestedUnit);

        if (requestedUnit.Contains("linear", StringComparison.OrdinalIgnoreCase) &&
            (match.Unit.Equals("Each", StringComparison.OrdinalIgnoreCase) || match.Unit.Equals("Piece", StringComparison.OrdinalIgnoreCase)) &&
            BoardLengthFeet(match.Description) is { } boardLength && boardLength > 0)
        {
            return (decimal.Ceiling(requestedQuantity / boardLength), match.Unit);
        }

        return (requestedQuantity, match.Unit);
    }

    private static decimal? BoardLengthFeet(string description)
    {
        var dimensional = Regex.Match(description, @"\b\d+(?:\.\d+)?\s*x\s*\d+(?:\.\d+)?\s*x\s*(?<length>\d+(?:\.\d+)?)\b", RegexOptions.IgnoreCase);
        if (dimensional.Success && decimal.TryParse(dimensional.Groups["length"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var dimensionalLength))
            return dimensionalLength;
        var explicitLength = Regex.Match(description, @"\b(?<length>\d+(?:\.\d+)?)\s*(?:ft\.?|foot|feet|')\b", RegexOptions.IgnoreCase);
        return explicitLength.Success && decimal.TryParse(explicitLength.Groups["length"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var length)
            ? length
            : null;
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
        var requestedCategory = MaterialCategory(proposed.Description);
        var requestedRole = MaterialRole(proposed.Description);
        var preferredCategoryMatch = catalog
            .Where(item => item.IsPreferred && requestedCategory is not null &&
                MaterialCategory($"{item.Category} {item.Description}") == requestedCategory &&
                BrandCompatible(proposed.Description, item.Description, item.ProductSystem) &&
                RoleCompatible(requestedRole, MaterialRole(item.Description)))
            .OrderByDescending(item => ProductSystemScore(proposed.Description, item.ProductSystem))
            .ThenByDescending(item => WordTokens(proposed.Description).Intersect(WordTokens(item.Description)).Count())
            .ThenBy(item => item.PreferencePriority)
            .ThenByDescending(item => IsPreferredVendor(item.VendorName))
            .ThenBy(item => item.UnitPrice)
            .FirstOrDefault();
        if (preferredCategoryMatch is not null) return preferredCategoryMatch;

        var familyCategoryMatch = catalog
            .Where(item => requestedCategory is not null &&
                MaterialCategory($"{item.Category} {item.Description}") == requestedCategory &&
                BrandCompatible(proposed.Description, item.Description, item.ProductSystem) &&
                RoleCompatible(requestedRole, MaterialRole(item.Description)))
            .Select(item => new
            {
                Item = item,
                SharedWords = WordTokens(proposed.Description).Intersect(WordTokens(item.Description)).Count(),
                SystemScore = ProductSystemScore(proposed.Description, item.ProductSystem)
            })
            .Where(candidate => candidate.SharedWords >= 2 || candidate.SystemScore >= 2)
            .OrderByDescending(candidate => candidate.SystemScore)
            .ThenByDescending(candidate => candidate.SharedWords)
            .ThenByDescending(candidate => candidate.Item.Description.Contains("grooved", StringComparison.OrdinalIgnoreCase))
            .ThenBy(candidate => candidate.Item.UnitPrice)
            .FirstOrDefault();
        if (familyCategoryMatch is not null) return familyCategoryMatch.Item;

        var candidates = catalog
        .Where(item => BrandCompatible(proposed.Description, item.Description, item.ProductSystem) &&
            RoleCompatible(requestedRole, MaterialRole(item.Description)))
        .Select(item =>
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
                @"(?<length>\d+(?:\.\d+)?)\s*(?:[’']?\s*[x×]|\bby\b)\s*(?<width>\d+(?:\.\d+)?)",
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
                .Where(item => MaterialCategory(item.Description) == "Decking")
                .Sum(item => item.Unit.Contains("linear", StringComparison.OrdinalIgnoreCase)
                    ? item.Quantity
                    : item.Quantity * (BoardLengthFeet(item.Description) ?? 12));
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
            .Select(match => match.Value is "enhanced" ? "enhance" : match.Value)
            .Where(word => word.Length >= 3 && word is not "the" and not "for" and not "with" and not "each")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string? MaterialCategory(string value) =>
        ContainsAny(value, "joist", "beam", "header", "framing lumber") ? "Framing" :
        ContainsAny(value, "decking", "deck board", "composite board") ||
            (value.Contains("trex", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(value, @"\b1\s*x\s*6\b", RegexOptions.IgnoreCase)) ? "Decking" :
        ContainsAny(value, "railing", "guardrail", "guard rail", "baluster", "rail cap") ||
            (value.Contains("trex enhance steel", StringComparison.OrdinalIgnoreCase) && ContainsAny(value, "panel", "post", "bracket")) ? "Railing" :
        ContainsAny(value, "footing", "concrete", "pier") ? "Footings" :
        ContainsAny(value, "hanger", "connector", "bracket", "post base", "anchor") ? "Connectors" :
        ContainsAny(value, "fastener", "screw", "nail", "bolt", "clip") ? "Fasteners" :
        ContainsAny(value, "flashing", "waterproof") ? "Flashing" :
        ContainsAny(value, "fascia", "rim board") ? "Fascia" :
        ContainsAny(value, "stair", "stringer", "tread") ? "Stairs" : null;

    private static string? MaterialRole(string value) =>
        ContainsAny(value, "cap and skirt", "post cap", "post skirt") ? "RailingCapSkirt" :
        ContainsAny(value, "railing bracket", "rail bracket") ||
            (value.Contains("trex enhance steel", StringComparison.OrdinalIgnoreCase) && value.Contains("bracket", StringComparison.OrdinalIgnoreCase)) ? "RailingBracket" :
        ContainsAny(value, "railing panel", "rail panel") ||
            (value.Contains("trex enhance steel", StringComparison.OrdinalIgnoreCase) && value.Contains("panel", StringComparison.OrdinalIgnoreCase)) ? "RailingPanel" :
        ContainsAny(value, "railing post", "rail post") ||
            (value.Contains("trex enhance steel", StringComparison.OrdinalIgnoreCase) && value.Contains("post", StringComparison.OrdinalIgnoreCase)) ? "RailingPost" :
        MaterialCategory(value);

    private static bool RoleCompatible(string? requestedRole, string? candidateRole) =>
        requestedRole is null || string.Equals(requestedRole, candidateRole, StringComparison.OrdinalIgnoreCase);

    private static bool BrandCompatible(string requested, string candidate, string? productSystem)
    {
        if (!requested.Contains("trex", StringComparison.OrdinalIgnoreCase)) return true;
        return candidate.Contains("trex", StringComparison.OrdinalIgnoreCase) ||
            (productSystem?.Contains("trex", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static int ProductSystemScore(string requested, string? system) =>
        string.IsNullOrWhiteSpace(system) ? 0 : WordTokens(requested).Intersect(WordTokens(system)).Count();

    private static bool IsPreferredVendor(string vendorName) =>
        vendorName.Equals("Decks & Docks", StringComparison.OrdinalIgnoreCase) ||
        vendorName.Equals("Deck & Docks", StringComparison.OrdinalIgnoreCase);

    private sealed record CatalogItem(
        int VendorProductId,
        string VendorName,
        string Sku,
        string Description,
        string? Category,
        string? ProductSystem,
        bool IsPreferred,
        int PreferencePriority,
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
