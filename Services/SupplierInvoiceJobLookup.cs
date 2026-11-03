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

    public static async Task PopulateAsync(ApplicationDbContext db, SupplierInvoicePayload payload, CancellationToken ct)
    {
        // Preserve only a single verified existing selection; never select among multiple matches.
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
        var name = Normalize(payload.YourReference);
        if (name.Length < 3)
        {
            payload.JobLookupMessage = "Your Reference does not contain a usable customer name.";
            return;
        }
        // Read the existing synchronized HCP interface data, never a different operation's jobs.
        var jobs = await db.HousecallProJobs.AsNoTracking()
            .Where(x => x.LocalOperationId == payload.LocalOperationId && x.JobNumber != null && x.JobNumber != "")
            .Select(x => new { x.CustomerName, x.JobNumber, x.LastSyncedAt }).ToListAsync(ct);
        var numberMatches = jobs.Where(x => Regex.IsMatch(payload.YourReference,
            @"(?<![\p{L}\p{N}])" + Regex.Escape(x.JobNumber!.Trim()) + @"(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))).ToList();
        var matches = numberMatches.Count > 0 ? numberMatches
            : jobs.Where(x => Normalize(x.CustomerName ?? "") == name).ToList();
        // A printed Job is a candidate too, but must exist in this operation's HCP data.
        var existingMatch = jobs.FirstOrDefault(x => x.JobNumber!.Trim().Equals(existing, StringComparison.OrdinalIgnoreCase));
        if (existingMatch is not null && !matches.Any(x => x.JobNumber == existingMatch.JobNumber)) matches.Add(existingMatch);
        payload.MatchedJobNumbers = matches.Select(x => x.JobNumber!.Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        payload.JobLookupSourceSyncedAt = matches.Count > 0 ? matches.Min(x => x.LastSyncedAt) : null;
        payload.JobNumber = existingMatch is not null ? existingMatch.JobNumber!.Trim()
            : payload.MatchedJobNumbers.Count == 1 ? payload.MatchedJobNumbers[0] : "";
        payload.JobLookupFilledValue = payload.JobNumber;
        payload.JobLookupMessage = payload.MatchedJobNumbers.Count == 0
            ? "No validated job match in synchronized Housecall Pro data for this location. Confirm the reference or the HCP synchronization before posting."
            : payload.MatchedJobNumbers.Count > 1
                ? "Multiple validated jobs found. Select exactly one job for this invoice before posting."
                : "One job validated in synchronized Housecall Pro data for this location. Confirm it belongs to this invoice.";
    }
}
