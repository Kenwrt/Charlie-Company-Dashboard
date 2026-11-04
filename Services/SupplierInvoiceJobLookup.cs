using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace CharleyCompany.Dashboard.Web.Services;

public static class SupplierInvoiceJobLookup
{
    private static string Normalize(string value) => string.Join(" ",
        new string(value.Normalize().Select(c => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string StreetKey(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return "";
        var match = Regex.Match(address.ToUpperInvariant(), @"\b\d+[A-Z]?\s+[^,\r\n]+", RegexOptions.CultureInvariant);
        if (!match.Success) return "";
        var street = Regex.Split(match.Value, @"\b(?:DRIVE|DR|STREET|ST|ROAD|RD|AVENUE|AVE|LANE|LN|COURT|CT|BOULEVARD|BLVD|CIRCLE|CIR|TRAIL|TRL|WAY|PLACE|PL|PIKE|PKWY|PARKWAY|APT|UNIT|SUITE)\b", RegexOptions.CultureInvariant)[0];
        return Normalize(street);
    }

    public static async Task PopulateAsync(ApplicationDbContext db, SupplierInvoicePayload payload, CancellationToken ct, HousecallProDataService hcp)
    {
        payload.JobUnassigned = false;
        payload.JobUnassignedReason = "";
        // Preserve only a single verified existing selection; never select among multiple matches.
        payload.YourReference = payload.YourReference.Trim();
        var existing = payload.JobNumber.Trim();
        payload.MatchedJobNumbers = [];
        payload.JobLookupLocationId = payload.LocalOperationId;
        payload.JobLookupReference = payload.YourReference;
        payload.JobLookupAt = DateTimeOffset.UtcNow;
        if (payload.LocalOperationId is null || payload.LocationRequiresReview)
        {
            payload.JobLookupMessage = "Resolve the purchasing location before matching customer jobs.";
            return;
        }
        // Supplier annotations such as customer-TREX are not part of the lookup.
        // Keep suffix text in YourReference; the immutable original payload retains source whitespace.
        var separator = payload.YourReference.IndexOfAny(['-', '\u2013', '\u2014']);
        var reference = (separator >= 0 ? payload.YourReference[..separator] : payload.YourReference).Trim();
        var name = Normalize(reference);
        // Read the existing synchronized HCP interface data, never a different operation's jobs.
        var operation = await db.LocalOperations.AsNoTracking().SingleAsync(x => x.Id == payload.LocalOperationId, ct);
        var jobs = await hcp.GetSupplierJobsAsync(operation.Slug, ct);
        var numberMatches = jobs.Where(x => Regex.IsMatch(reference,
            @"(?<![\p{L}\p{N}])" + Regex.Escape(x.JobNumber!.Trim()) + @"(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))).ToList();
        var matches = numberMatches.Count > 0 ? numberMatches
            : jobs.Where(x => name.Length > 0 && Normalize(x.CustomerName ?? "") == name).ToList();
        var partialMatch = false;
        var matchDescription = "";
        if (matches.Count == 0)
        {
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // Whole name components only: never match a substring inside a surname.
            var lastName = parts.LastOrDefault() ?? "";
            if (lastName.Length >= 2 && lastName.All(char.IsLetter))
            {
                matches = jobs.Where(x => Normalize(x.CustomerName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .LastOrDefault() == lastName).ToList();
                if (matches.Count > 0) matchDescription = "Last-name-only match";
            }
            if (matches.Count == 0)
            {
                var firstName = parts.FirstOrDefault() ?? "";
                if (firstName.Length >= 2 && firstName.All(char.IsLetter))
                {
                    matches = jobs.Where(x => Normalize(x.CustomerName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault() == firstName).ToList();
                    if (matches.Count > 0) matchDescription = "First-name-only match";
                }
            }
            partialMatch = matches.Count > 0;
        }
        if (matches.Count == 0)
        {
            var street = StreetKey(payload.DeliveryAddress);
            if (street.Length > 0)
            {
                matches = jobs.Where(x => StreetKey(x.Address) == street).ToList();
                if (matches.Count > 0) { partialMatch = true; matchDescription = "Delivery-address match"; }
            }
        }
        // A printed Job is a candidate too, but must exist in this operation's HCP data.
        var existingMatch = jobs.FirstOrDefault(x => x.JobNumber!.Trim().Equals(existing, StringComparison.OrdinalIgnoreCase));
        if (existingMatch is not null && !matches.Any(x => x.JobNumber == existingMatch.JobNumber)) matches.Add(existingMatch);
        payload.MatchedJobNumbers = matches.Select(x => x.JobNumber!.Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        payload.JobOptionLabels = matches.GroupBy(x => x.JobNumber.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => string.Join(" | ", g.Select(x => x.Description + " - " + x.CustomerName + " - " + x.Address).Distinct()), StringComparer.OrdinalIgnoreCase);
        payload.JobLookupSourceSyncedAt = matches.Count > 0 ? matches.Min(x => x.LastSyncedAt) : null;
        payload.JobNumber = existingMatch is not null ? existingMatch.JobNumber!.Trim()
            : payload.MatchedJobNumbers.Count == 1 ? payload.MatchedJobNumbers[0] : "";
        if (payload.MatchedJobNumbers.Count == 0)
        {
            payload.JobUnassigned = true;
            payload.JobUnassignedReason = string.IsNullOrWhiteSpace(payload.YourReference)
                ? "Your Reference is blank; job assignment deferred."
                : "No job found in this location's Housecall Pro API data after checking reference job numbers, full name, last name, first name and delivery address; job assignment deferred.";
        }
        payload.JobLookupFilledValue = payload.JobNumber;
        payload.JobLookupMessage = payload.MatchedJobNumbers.Count == 0
            ? payload.JobUnassignedReason + " The invoice will enter the unassigned job expenses queue when approved."
            : partialMatch
                ? matchDescription + (payload.MatchedJobNumbers.Count == 1
                    ? ". The only matching job was selected automatically. Confirm the customer and job before approving."
                    : ". Confirm the customer in Housecall Pro and select exactly one job.")
            : payload.MatchedJobNumbers.Count > 1
                ? "Multiple validated jobs found. Select exactly one job for this invoice before posting."
                : "One job validated in Housecall Pro API data for this location. Confirm it belongs to this invoice.";
    }
}
