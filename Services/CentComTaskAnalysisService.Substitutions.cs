using System.Text.Json;
using System.Text;
using CharleyCompany.Dashboard.Web.Data;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed partial class CentComTaskAnalysisService
{
    private const string CopiedOptionResponseInstructions = """
        COPIED-OPTION OVERRIDE:
        Analyze every line of the supplied frozen source material list.
        The selected substitution instructions override conflicting wood specifications in inherited scope or description.
        Keep unrelated products, quantities, units, waste, and costs unchanged. Do not redesign framing or foundations.
        Return sourceMaterialActions with exactly one entry for EVERY source line:
          {"sourceLineNumber":1,"action":"keep|replace|remove","substitutionKey":"selected key or null","reason":"short explanation"}
        Source line numbers are one-based. Replace wood boards or rail assemblies only for the selected substitutions.
        Associated obsolete fasteners/finishes can be removed; explain every change.
        The materials array contains ONLY replacement and additional products, never retained source products.
        Each new material must add:
          "substitutionKey": "selected substitution key",
          "replacesSourceLines": [source line numbers affected by this replacement or accessory]
        Every replaced source line must be referenced by at least one new material.
        Include all compatible accessories and installation quantities for the replacement system.
        Do not propose wood decking for a Trex/Deckorators substitution or wood rails for a vinyl substitution.
        Do not remove wood joists, beams, ledgers, support posts, stringers, footings, or unrelated materials.
        Preserve copied standard supplies without adding duplicate supply kits.
        With no substitutions selected, mark every source line keep and return an empty materials array.
        Keep the other JSON fields from the normal schema. Allowances describe the COMPLETE resulting option,
        not additional copies of source costs. Include unresolved compatibility/design concerns in warnings.
        """;

    private static string BuildCopiedOptionPrompt(EstimateOption? option)
    {
        if (option?.CopySource is not { } source) return "";
        var builder = new StringBuilder()
            .AppendLine().AppendLine("FROZEN COPY SOURCE (material rows, not inherited narrative, define the starting assembly):")
            .AppendLine($"Source option: {source.Name}; source analysis: {source.AnalysisId}")
            .AppendLine("SOURCE MATERIALS:")
            .AppendLine(JsonSerializer.Serialize(source.Materials.Select((material, index) => new
            {
                SourceLineNumber = index + 1, material.Description, material.VendorName, material.VendorSku,
                material.Quantity, material.Unit, material.WastePercent, material.UnitCost, material.IsPolicySupply
            })))
            .AppendLine("SELECTED SUBSTITUTIONS:");
        foreach (var key in option.Substitutions)
        {
            var rule = EstimateOptionSubstitutions.All.Single(item => item.Key == key);
            builder.AppendLine($"{rule.Key}: {rule.Instruction}");
        }
        if (option.Substitutions.Count == 0) builder.AppendLine("None. Retain the complete copied material list.");
        builder.AppendLine($"Starting additional baseline costs: {source.AdditionalBaselineCost}. Keep these in otherAllowance unless a selected substitution changes them; explain any adjustment.")
            .AppendLine(CopiedOptionResponseInstructions);
        return builder.ToString();
    }

    private static IReadOnlyList<EstimateOptionMaterial> ReconcileCopiedMaterials(EstimateOption option, AnalysisResponse result)
    {
        var source = option.CopySource ?? throw new InvalidOperationException("A source material snapshot is required.");
        var actions = result.SourceMaterialActions;
        if (actions.Count != source.Materials.Count
            || !actions.Select(item => item.SourceLineNumber).ToHashSet().SetEquals(Enumerable.Range(1, source.Materials.Count)))
            throw new InvalidOperationException("CentCom must account for every copied material exactly once.");

        var retained = new List<EstimateOptionMaterial>();
        var byLine = actions.ToDictionary(item => item.SourceLineNumber);
        var changes = new List<string>();
        foreach (var action in actions)
        {
            action.Action = (action.Action ?? "").Trim().ToLowerInvariant();
            var material = source.Materials[action.SourceLineNumber - 1];
            if (action.Action == "keep")
            {
                if (option.Substitutions.Any(key => EstimateOptionSubstitutions.IsPrimarySource(material.Description, key)))
                    throw new InvalidOperationException($"The selected substitution still retained '{material.Description}'.");
                retained.Add(material);
                continue;
            }
            if (action.Action is not "replace" and not "remove"
                || action.SubstitutionKey is null || !option.Substitutions.Contains(action.SubstitutionKey)
                || string.IsNullOrWhiteSpace(action.Reason)
                || !EstimateOptionSubstitutions.IsRelatedSource(material.Description, action.SubstitutionKey))
                throw new InvalidOperationException("A source material was changed outside the selected substitution.");
            if (EstimateOptionSubstitutions.IsPrimarySource(material.Description, action.SubstitutionKey) && action.Action != "replace")
                throw new InvalidOperationException("The original decking or railing must be replaced, not simply removed.");
            if (action.Action == "replace" && !result.Materials.Any(item => item.ReplacesSourceLines.Contains(action.SourceLineNumber)
                && item.SubstitutionKey == action.SubstitutionKey))
                throw new InvalidOperationException($"No replacement was supplied for '{material.Description}'.");
            changes.Add($"[MATERIAL SUBSTITUTION] {action.Action}: {material.Description}. {action.Reason}");
        }
        foreach (var material in result.Materials)
        {
            if (material.SubstitutionKey is null || !option.Substitutions.Contains(material.SubstitutionKey)
                || material.Quantity <= 0 || string.IsNullOrWhiteSpace(material.Description) || string.IsNullOrWhiteSpace(material.Unit)
                || material.ReplacesSourceLines.Count == 0 || material.ReplacesSourceLines.Any(line =>
                    !byLine.TryGetValue(line, out var action) || action.Action == "keep" || action.SubstitutionKey != material.SubstitutionKey))
                throw new InvalidOperationException("Every new material must belong to a selected substitution and reference affected source lines.");
            if (option.Substitutions.Any(key => EstimateOptionSubstitutions.IsPrimarySource(material.Description, key)))
                throw new InvalidOperationException("CentCom added a wood material that the selected substitution should replace.");
            if (MaterialCategory(material.Description) is "Framing" or "Footings")
                throw new InvalidOperationException("A finish-material substitution cannot add structural framing or foundation work.");
            if (retained.Any(item => NormalizeReviewText(item.Description) == NormalizeReviewText(material.Description)))
                throw new InvalidOperationException("CentCom duplicated a retained source material.");
        }
        foreach (var key in option.Substitutions)
        {
            if (!actions.Any(item => item.Action == "replace" && item.SubstitutionKey == key
                    && EstimateOptionSubstitutions.IsPrimarySource(source.Materials[item.SourceLineNumber - 1].Description, key))
                || !result.Materials.Any(item => item.SubstitutionKey == key && IsRequestedReplacement(item.Description, key)))
                throw new InvalidOperationException($"CentCom did not supply the requested replacement: {EstimateOptionSubstitutions.Label(key)}.");
        }
        if (option.Substitutions.Count == 0)
        {
            result.DeliveryAllowance = 0;
            result.TaxAllowance = 0;
            result.OtherAllowance = source.AdditionalBaselineCost;
        }
        result.Warnings.AddRange(changes);
        return retained;
    }

    private static bool IsRequestedReplacement(string description, string key)
    {
        if (ContainsAny(description, "fastener", "screw", "clip", "bracket", "cap", "skirt")) return false;
        return key switch
        {
            EstimateOptionSubstitutions.Trex => description.Contains("trex", StringComparison.OrdinalIgnoreCase)
                && MaterialCategory(description) == "Decking",
            EstimateOptionSubstitutions.Deckorators => ContainsAny(description, "deckorator", "decorator")
                && MaterialCategory(description) == "Decking",
            EstimateOptionSubstitutions.Vinyl => ContainsAny(description, "vinyl", "pvc")
                && ContainsAny(description, "railing", "rail panel", "rail kit"),
            _ => false
        };
    }

    private sealed class SourceMaterialAction
    {
        public int SourceLineNumber { get; set; }
        public string Action { get; set; } = "";
        public string? SubstitutionKey { get; set; }
        public string Reason { get; set; } = "";
    }
}
