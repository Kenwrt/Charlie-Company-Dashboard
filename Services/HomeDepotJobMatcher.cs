using System.Text.RegularExpressions;
using CharleyCompany.Dashboard.Web.Models;

namespace CharleyCompany.Dashboard.Web.Services;

/// <summary>Interprets source references as job numbers, street addresses or names; proposes candidates only.</summary>
public static class HomeDepotJobMatcher
{
    private static readonly Dictionary<string, string> StreetWords = new(StringComparer.Ordinal)
    {
        ["ST"] = "STREET", ["STREET"] = "STREET", ["RD"] = "ROAD", ["ROAD"] = "ROAD",
        ["DR"] = "DRIVE", ["DRIVE"] = "DRIVE", ["AVE"] = "AVENUE", ["AVENUE"] = "AVENUE",
        ["LN"] = "LANE", ["LANE"] = "LANE", ["CT"] = "COURT", ["COURT"] = "COURT",
        ["BLVD"] = "BOULEVARD", ["BOULEVARD"] = "BOULEVARD", ["CIR"] = "CIRCLE", ["CIRCLE"] = "CIRCLE",
        ["TRL"] = "TRAIL", ["TRAIL"] = "TRAIL", ["PL"] = "PLACE", ["PLACE"] = "PLACE",
        ["PKWY"] = "PARKWAY", ["PARKWAY"] = "PARKWAY", ["PIKE"] = "PIKE", ["WAY"] = "WAY",
        ["TER"] = "TERRACE", ["TERRACE"] = "TERRACE", ["HWY"] = "HIGHWAY", ["HIGHWAY"] = "HIGHWAY"
    };
    private static readonly Dictionary<string, string> Directions = new(StringComparer.Ordinal)
    {
        ["N"] = "NORTH", ["S"] = "SOUTH", ["E"] = "EAST", ["W"] = "WEST",
        ["NE"] = "NORTHEAST", ["NW"] = "NORTHWEST", ["SE"] = "SOUTHEAST", ["SW"] = "SOUTHWEST"
    };
    private sealed record Street(string House, string Name, string Suffix, string Unit);
    private sealed record Evidence(int Rank, string Kind, string Explanation, string Source);
    private static string Normalize(string? value) => HomeDepotHistoryText.Normalize(value ?? "");
    private static Match Match(string value, string pattern) => Regex.Match(value, pattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static Street? Address(string value)
    {
        var first = value.Trim().Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        first = Regex.Replace(first, @"^\s*(?:ADDRESS|DELIVERY ADDRESS|JOB ADDRESS)\s*[:=-]?\s*", "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var text = Normalize(first.Replace("#", " UNIT ", StringComparison.Ordinal));
        var match = Match(text, @"^(?<house>\d+[A-Z]?)\s+(?<street>.+)$");
        if (!match.Success || !match.Groups["street"].Value.Any(char.IsLetter)) return null;
        var parts = match.Groups["street"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var unitAt = parts.FindIndex(x => x is "APT" or "APARTMENT" or "UNIT" or "STE" or "SUITE");
        var unit = unitAt >= 0 ? string.Join(" ", parts.Skip(unitAt + 1)) : "";
        if (unitAt >= 0) parts = parts.Take(unitAt).ToList();
        var suffixAt = parts.FindLastIndex(x => StreetWords.ContainsKey(x));
        var suffix = suffixAt >= 0 ? StreetWords[parts[suffixAt]] : "";
        if (suffixAt >= 0) parts = parts.Take(suffixAt).ToList();
        var name = string.Join(" ", parts.Select(x => Directions.GetValueOrDefault(x, x)));
        return name.Length == 0 ? null : new(match.Groups["house"].Value, name, suffix, unit);
    }

    public static List<HomeDepotCandidate> Find(IEnumerable<string> rawReferences, IEnumerable<HcpSupplierJob> jobs,
        int operationId, string operation)
    {
        var references = rawReferences.Where(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 2000)
            .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var results = new List<HomeDepotCandidate>();
        foreach (var job in jobs)
        {
            var evidence = new List<Evidence>();
            var jobNumber = job.JobNumber.Trim();
            var customer = Normalize(job.CustomerName);
            var customerParts = customer.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var jobStreet = Address(job.Address);
            foreach (var raw in references)
            {
                var text = Normalize(raw);
                var street = Address(raw);
                var explicitNumbers = Regex.Matches(raw, @"\b(?:JOB(?:\s+(?:NO\.?|NUMBER))?|PROJECT(?:\s+(?:NO\.?|NUMBER))?|PO)\s*[:#=-]?\s*(?<n>[A-Z0-9]+(?:-[A-Z0-9]+)*)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
                    .Select(x => x.Groups["n"].Value).ToList();
                var exactNumber = jobNumber.Length > 0 && (explicitNumbers.Contains(jobNumber, StringComparer.OrdinalIgnoreCase)
                    || street is null && Match(raw, @"(?<![\p{L}\p{N}])" + Regex.Escape(jobNumber) + @"(?![\p{L}\p{N}])").Success);
                if (exactNumber) evidence.Add(new(100, "Job number", "Whole job number found in the source reference and validated against this operation's HCP jobs.", raw));
                // A house number is never treated as a job number merely because its digits match.
                if (street is not null && jobStreet is not null && street.House == jobStreet.House && street.Name == jobStreet.Name)
                {
                    var suffixConflict = street.Suffix.Length > 0 && jobStreet.Suffix.Length > 0 && street.Suffix != jobStreet.Suffix;
                    var unitConflict = street.Unit.Length > 0 && jobStreet.Unit.Length > 0 && street.Unit != jobStreet.Unit;
                    if (!suffixConflict && !unitConflict)
                    {
                        var incomplete = street.Suffix.Length == 0 || jobStreet.Suffix.Length == 0
                            || (street.Unit.Length == 0) != (jobStreet.Unit.Length == 0);
                        evidence.Add(incomplete
                            ? new(80, "Partial address", "Street number/name match, but a street suffix or unit is missing; confirm the full address.", raw)
                            : new(95, "Street address", "Street number/name, normalized street suffix and any supplied unit match the HCP job address; city/ZIP still require review.", raw));
                    }
                }
                if (street is not null) continue; // Do not turn street-name words into homeowner-name guesses.
                if (customer.Length > 0 && text == customer)
                    evidence.Add(new(90, "Homeowner name", "Exact normalized homeowner/customer name; this customer may have more than one job.", raw));
                else if (customerParts.Length > 0)
                {
                    var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var last = customerParts[^1]; var first = customerParts[0];
                    if (last.Length >= 2 && last.All(char.IsLetter) && parts.Contains(last))
                        evidence.Add(new(60, "Last-name candidate", "Whole homeowner last-name component only; confirm the customer, address and project.", raw));
                    if (first.Length >= 2 && first.All(char.IsLetter) && parts.Contains(first))
                        evidence.Add(new(50, "First-name candidate", "Whole homeowner first-name component only; confirm the customer, address and project.", raw));
                }
            }
            if (evidence.Count == 0) continue;
            var ordered = evidence.OrderByDescending(x => x.Rank).ToList();
            var best = ordered[0];
            results.Add(new(job.ExternalId, job.JobNumber, job.CustomerName, job.Address, job.Description,
                operationId, operation, string.Join(" ", ordered.Select(x => x.Explanation).Distinct()), job.CustomerId,
                best.Rank, best.Kind, best.Source));
        }
        return results.OrderByDescending(x => x.Rank).ThenBy(x => x.JobNumber, StringComparer.Ordinal)
            .ThenBy(x => x.JobId, StringComparer.Ordinal).ToList();
    }
}
