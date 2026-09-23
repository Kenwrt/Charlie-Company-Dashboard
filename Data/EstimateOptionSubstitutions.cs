using System.Text.RegularExpressions;

namespace CharleyCompany.Dashboard.Web.Data;

// A flat snapshot avoids mutable links or recursive copies from option 3 to 2 to 1.
public sealed class EstimateOptionCopySource
{
    public Guid OptionId { get; set; }
    public int TaskId { get; set; }
    public int VersionId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int? AnalysisId { get; set; }
    public decimal AdditionalBaselineCost { get; set; }
    public DateTimeOffset CopiedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<EstimateOptionMaterial> Materials { get; set; } = [];
}

public sealed record EstimateMaterialSubstitution(string Key, string Label, string Instruction);

public static class EstimateOptionSubstitutions
{
    public const string Trex = "wood-decking-to-trex";
    public const string Deckorators = "wood-decking-to-deckorators";
    public const string Vinyl = "wood-railing-to-vinyl";
    public static readonly IReadOnlyList<EstimateMaterialSubstitution> All =
    [
        new(Trex, "Replace wood decking with Trex decking",
            "Replace wood surface decking boards with Trex decking. Include compatible decking fasteners, clips, edge boards, and fascia where needed. Retain the structural wood framing."),
        new(Deckorators, "Replace wood decking with Deckorators decking",
            "Replace wood surface decking boards with Deckorators decking. Include compatible decking fasteners, clips, edge boards, and fascia where needed. Retain the structural wood framing."),
        new(Vinyl, "Replace wood railing with vinyl railing",
            "Replace the wood railing assembly with vinyl railing, including compatible rail sections, balusters, sleeves, caps, brackets, and hardware. Retain structural support posts and framing unless separately specified.")
    ];

    public static bool CanApply(EstimateOptionCopySource? source, string key) =>
        source is not null && source.Materials.Any(material => IsPrimarySource(material.Description, key));

    public static bool IsPrimarySource(string description, string key)
    {
        if (Contains(description, "joist", "beam", "ledger", "footing", "structural post", "support post", "stringer")) return false;
        var wood = Contains(description, "wood", "cedar", "pine", "redwood", "treated")
            || Regex.IsMatch(description, @"\b(?:5\s*/\s*4|2\s*x\s*2|2\s*x\s*4|4\s*x\s*4)\b", RegexOptions.IgnoreCase);
        if (!wood || Contains(description, "composite", "trex", "deckorator", "vinyl", "pvc", "aluminum", "steel")) return false;
        return key switch
        {
            Trex or Deckorators => Contains(description, "decking", "deck board", "surface board")
                || Regex.IsMatch(description, @"\b5\s*/\s*4\b", RegexOptions.IgnoreCase),
            Vinyl => Contains(description, "railing", "rail ", "guardrail", "baluster", "picket", "spindle"),
            _ => false
        };
    }

    public static bool IsRelatedSource(string description, string key)
    {
        if (IsPrimarySource(description, key)) return true;
        if (Contains(description, "joist", "beam", "ledger", "footing", "structural post", "support post", "stringer")) return false;
        return key switch
        {
            Trex or Deckorators => Contains(description, "deck screw", "decking screw", "deck fastener", "decking fastener",
                "deck stain", "wood stain", "deck sealer", "decking clip", "deck clip", "fascia"),
            Vinyl => Contains(description, "rail bracket", "railing bracket", "rail post", "railing post", "rail cap", "railing cap",
                "rail hardware", "railing hardware", "baluster", "picket", "spindle"),
            _ => false
        };
    }

    public static string Label(string key) => All.Single(item => item.Key == key).Label;
    private static bool Contains(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}
