using System.Text.RegularExpressions;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class SupplierInvoiceRouting(IOptions<SupplierInvoiceOptions> options)
{
    public async Task<SupplierInvoiceLocation> ResolveAsync(ApplicationDbContext db, string reference, CancellationToken ct)
    {
        var operations = await db.LocalOperations.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
        return Resolve(reference, operations);
    }

    public SupplierInvoiceLocation Resolve(string reference, IReadOnlyCollection<LocalOperation> operations)
    {
        var settings = options.Value;
        var text = reference.Trim().ToUpperInvariant();
        var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recognizedCode = false;
        foreach (var (slug, aliases) in settings.LocationAliases)
        foreach (var alias in aliases)
        {
            var normalized = Normalize(alias);
            if (normalized.Length == 0) continue;
            // Ignore casing, spaces, dots, dashes and brackets; require whole tokens.
            var end = normalized.Length == 3 ? @"(?=$|[^\p{L}\p{N}]|\d)" : @"(?![\p{L}\p{N}])";
            var pattern = @"(?<![\p{L}\p{N}])" + string.Join(@"[\W_]*", normalized.Select(c => Regex.Escape(c.ToString()))) + end;
            if (Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            {
                matches.Add(slug);
                if (normalized.Length == 3) recognizedCode = true;
            }
        }
        if (matches.Count > 1)
            return new(null, "ConflictingReference", "Your Reference identifies more than one location. Select the location from the original order or job record.", true);

        // Typo proximity is a warning only, never an automatic correction.
        var tokens = Regex.Matches(text, @"[A-Z]{5,}").Select(m => m.Value).ToList();
        var possibleTypo = settings.LocationAliases.Values.SelectMany(x => x).Select(Normalize)
            .Where(x => x.Length >= 6).Any(alias => tokens.Any(token => token != alias
                && Math.Abs(token.Length - alias.Length) <= 2 && DistanceAtMostTwo(token, alias)));
        var codes = settings.LocationAliases.Values.SelectMany(x => x).Select(Normalize).Where(x => x.Length == 3).Distinct().ToList();
        var shortTokens = Regex.Matches(text, @"\b[A-Z]{2,4}(?=\b|\d)").Select(m => m.Value).ToList();
        var possibleCodeTypo = shortTokens.Any(token => !codes.Contains(token) && codes.Any(code =>
            Distance(token, code) <= 1 || token.Length == 3 && ((token[0] == code[1] && token[1] == code[0] && token[2] == code[2])
                || (token[0] == code[0] && token[1] == code[2] && token[2] == code[1]))));
        var explicitUnknownCode = !recognizedCode && (Regex.IsMatch(text, @"^\s*(?:\[\s*)?[A-Z]{3}\s*(?:\]|[|:;/-])")
            || Regex.IsMatch(text, @"\bLOC(?:ATION)?\s*[:=#]"));
        if (possibleTypo || possibleCodeTypo || explicitUnknownCode && matches.Count == 0)
            return new(null, "UncertainReference", "Your Reference may contain an incorrect location name or code. It was not guessed or assigned to Nashville.", true);

        string? target;
        string source;
        if (matches.Count == 1)
        {
            target = matches.Single(); source = "YourReference";
            if (!settings.ParticipatingLocationSlugs.Contains(target, StringComparer.OrdinalIgnoreCase))
                return new(null, source, "The reference names a location not yet configured to use this supplier account.", true);
        }
        else if (settings.AllowSingleLocationDefault && settings.ParticipatingLocationSlugs.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
            && settings.ParticipatingLocationSlugs.Contains(settings.DefaultLocationSlug, StringComparer.OrdinalIgnoreCase))
        {
            target = settings.DefaultLocationSlug; source = "SingleLocationDefault";
        }
        else
            return new(null, "MissingReference", "No unambiguous location was identified. The Nashville fallback is disabled when this account serves multiple locations.", true);

        var operation = operations.SingleOrDefault(x => x.IsActive && x.Slug.Equals(target, StringComparison.OrdinalIgnoreCase));
        if (operation is null)
            return new(null, source, "The assigned location is missing or inactive. Review its operation mapping.", true);
        return new(operation.Id, source, source == "SingleLocationDefault"
            ? "Nashville default applies while only that location uses this supplier account."
            : "Location identified from an exact configured code or full city name; punctuation and spacing were ignored.", false);
    }

    private static string Normalize(string text) => string.Concat(text.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    private static bool DistanceAtMostTwo(string left, string right) => Distance(left, right) <= 2;
    private static int Distance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1]; current[0] = i;
            for (var j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[^1];
    }
}

public sealed record SupplierInvoiceLocation(int? OperationId, string Source, string Reason, bool RequiresReview);
