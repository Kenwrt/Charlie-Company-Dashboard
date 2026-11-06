using System.Security.Claims;
using System.Text.Json;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed record HomeDepotJobGroup(string Reference, int Purchases, int Lines, int Pending,
    string Status, List<HomeDepotCandidate> Candidates);

public sealed class HomeDepotJobGroupsService(HomeDepotHistoryStore store,
    IDbContextFactory<ApplicationDbContext> factory, HousecallProDataService hcp)
{
    private static string Ref(HomeDepotLine line) => HomeDepotSupplyIntakeService.Reference(line);
    private static bool Linked(HomeDepotHistoryState state, HomeDepotPurchase purchase, HomeDepotLine line)
    {
        var allocations = purchase.Review.MaterialAllocations.Where(a => a.LineId == line.Id).ToList();
        if (allocations.Count > 0) return allocations.Sum(a => a.Quantity) == line.Amount("Quantity")
            && allocations.Sum(a => a.ReportedAmount) == line.Amount("Extended Retail (before discount)");
        return Valid(line) && line.Date is DateOnly date && state.JobReferenceLinks.Any(x => x.SourceReference == Ref(line)
            && date >= x.FromDate && date <= x.ToDate
            && (purchase.Review.OperationId is null || purchase.Review.OperationId == x.OperationId));
    }
    private static IEnumerable<(HomeDepotPurchase Purchase, HomeDepotLine Line)> Rows(HomeDepotHistoryState state) =>
        state.Purchases.Where(p => p.Review.Status != "Excluded")
            .SelectMany(p => p.ReportingLines.Select(l => (Purchase: p, Line: l)));
    public static List<HomeDepotJobGroup> Groups(HomeDepotHistoryState state) => Rows(state).GroupBy(x => Ref(x.Line))
        .Select(g => {
            var pending = g.Count(x => !Linked(state, x.Purchase, x.Line));
            var candidates = g.SelectMany(x => x.Purchase.Review.Candidates)
                .Where(c => HomeDepotHistoryText.Normalize(c.MatchedReference) == g.Key)
                .DistinctBy(c => (c.OperationId, c.JobId)).OrderByDescending(c => c.Rank).ToList();
            var incomplete = g.Any(x => x.Purchase.Review.LookupStatus is "NotRequested" or "IncompleteOperationCoverage" or "IncompleteOrUnavailable");
            return new HomeDepotJobGroup(g.Key, g.Select(x => x.Purchase.Id).Distinct().Count(), g.Count(), pending,
                pending == 0 ? "Assigned" : incomplete ? "Search incomplete — refresh"
                : candidates.Count == 0 ? "No matching job" : candidates.Count == 1 ? "Unique candidate — source/job data needs attention" : "Choose job / review source", candidates);
        }).OrderBy(g => g.Reference).ToList();

    private async Task<List<(LocalOperation Operation, List<HcpSupplierJob> Jobs)>> Sources(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var operations = await db.LocalOperations.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
        var sources = new List<(LocalOperation, List<HcpSupplierJob>)>();
        // Any unavailable operation fails the whole refresh. Partial search must
        // never turn an apparently unique candidate into an automatic assignment.
        foreach (var op in operations) sources.Add((op, await hcp.GetSupplierJobsAsync(op.Slug, ct)));
        return sources;
    }
    public async Task<List<HomeDepotCandidate>> FindAsync(string query, ClaimsPrincipal user, CancellationToken ct)
    {
        HomeDepotHistoryService.Administrator(user);
        if (string.IsNullOrWhiteSpace(query) || query.Length > 2000) throw new ArgumentException("Enter a job number, name or address.");
        return (await Sources(ct)).SelectMany(s => HomeDepotJobMatcher.Find([query], s.Jobs, s.Operation.Id, s.Operation.EffectiveDisplayName)).OrderByDescending(c => c.Rank).ToList();
    }
    private static bool Valid(HomeDepotLine line) => line.Amount("Quantity") is decimal quantity && quantity != 0
        && decimal.Round(quantity, 4) == quantity && line.Amount("Extended Retail (before discount)") is decimal amount
        && decimal.Round(amount, 2) == amount;
    private static int Save(HomeDepotHistoryState state, string reference, HomeDepotCandidate candidate, string actor, string reason)
    {
        var count = 0;
        foreach (var group in Rows(state).Where(x => Ref(x.Line) == reference && !Linked(state, x.Purchase, x.Line)).GroupBy(x => x.Purchase))
        {
            var purchase = group.Key;
            if (purchase.Review.OperationId is int op && op != candidate.OperationId
                || purchase.Review.Conflicts.Count > 0 || group.Any(x => !Valid(x.Line)
                    || purchase.Review.MaterialAllocations.Any(a => a.LineId == x.Line.Id)))
                throw new ArgumentException("This group has conflicting source data or missing quantities/amounts. Resolve it in transaction review first.");
            var before = JsonSerializer.Serialize(purchase.Review.MaterialAllocations, HomeDepotHistoryText.Json);
            foreach (var row in group)
            {
                purchase.Review.MaterialAllocations.Add(new(row.Line.Id, candidate.OperationId, candidate.JobId,
                    candidate.JobNumber, row.Line.Amount("Quantity")!.Value, row.Line.Amount("Extended Retail (before discount)")!.Value));
                count++;
            }
            purchase.Version++; purchase.ChangedAt = DateTimeOffset.UtcNow;
            state.Audit.Add(new(DateTimeOffset.UtcNow, actor, "MaterialGroupAllocated", purchase.Id, before,
                JsonSerializer.Serialize(purchase.Review.MaterialAllocations, HomeDepotHistoryText.Json), reason));
        }
        return count;
    }
    public async Task<int> RefreshAndAssignAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var actor = HomeDepotHistoryService.Administrator(user);
        var sources = await Sources(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var synced = await db.HousecallProJobs.AsNoTracking().ToListAsync(ct);
        return await store.ChangeAsync(state => {
            var matches = Rows(state).Select(x => Ref(x.Line)).Distinct().ToDictionary(r => r,
                r => string.IsNullOrWhiteSpace(r) || state.DeletedCustomerReferences.Any(d => d.Reference == r) ? new List<HomeDepotCandidate>()
                    : sources.SelectMany(s => HomeDepotJobMatcher.Find([r], s.Jobs, s.Operation.Id, s.Operation.EffectiveDisplayName)).ToList());
            foreach (var purchase in state.Purchases.Where(p => p.Review.Status != "Excluded"))
            {
                purchase.Review.Candidates = purchase.ReportingLines.Select(Ref).Distinct().SelectMany(r => matches[r]).ToList();
                purchase.Review.LookupAt = DateTimeOffset.UtcNow;
                purchase.Review.LookupStatus = "CandidatesRequireReview";
                purchase.Version++;
            }
            var total = 0;
            foreach (var entry in matches)
            {
                var candidates = entry.Value;
                var confirmed = state.CustomerReviews.SingleOrDefault(x => x.Reference == entry.Key);
                if (confirmed is not null) candidates = candidates.Where(c => c.OperationId == confirmed.OperationId && c.CustomerId == confirmed.CustomerId).ToList();
                if (candidates.Count == 0) continue;
                var best = candidates.Where(c => c.Rank == candidates.Max(x => x.Rank)).DistinctBy(c => (c.OperationId, c.JobId)).ToList();
                if (best.Count != 1) continue;
                var candidate = best[0];
                var identities = synced.Where(j => j.LocalOperationId == candidate.OperationId && j.JobNumber?.Trim() == candidate.JobNumber.Trim()).ToList();
                if (identities.Count != 1 || identities[0].ExternalId != candidate.JobId) continue;
                var rows = Rows(state).Where(x => Ref(x.Line) == entry.Key).ToList();
                if (rows.Any(x => x.Purchase.Review.Conflicts.Count > 0 || !Valid(x.Line)
                    || x.Purchase.Review.OperationId is int op && op != candidate.OperationId
                    || !Linked(state, x.Purchase, x.Line) && x.Purchase.Review.MaterialAllocations.Any(a => a.LineId == x.Line.Id)
                    || x.Purchase.Review.MaterialAllocations.Any(a => a.LineId == x.Line.Id && (a.OperationId != candidate.OperationId || a.JobId != candidate.JobId)))
                    || state.JobReferenceLinks.Any(x => x.SourceReference == entry.Key && (x.OperationId != candidate.OperationId || x.JobId != candidate.JobId))) continue;
                total += Save(state, entry.Key, candidate, actor, "Automatically assigned unique strongest name/address/job-number match across complete operation-scoped HCP jobs. Existing assignments preserved.");
            }
            state.Audit.Add(new(DateTimeOffset.UtcNow, actor, "JobGroupSearchAndAssignment", null, "", total.ToString(), "Owner-authorized unique job matching; ambiguous and absent jobs remain in group review. No accounting changes."));
            return Task.FromResult(total);
        }, ct);
    }
    public async Task<int> AssignAsync(string reference, int operationId, string jobId, long revision,
        string reason, ClaimsPrincipal user, CancellationToken ct)
    {
        var actor = HomeDepotHistoryService.Administrator(user);
        if (reason.Trim().Length is < 5 or > 2000) throw new ArgumentException("Enter a review reason of 5–2000 characters.");
        var sources = await Sources(ct);
        var source = sources.SingleOrDefault(x => x.Operation.Id == operationId);
        var job = source.Jobs?.SingleOrDefault(x => x.ExternalId == jobId) ?? throw new ArgumentException("Job could not be confirmed in the selected operation.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var identities = await db.HousecallProJobs.AsNoTracking().Where(x => x.LocalOperationId == operationId && x.JobNumber == job.JobNumber).ToListAsync(ct);
        if (identities.Count != 1 || identities[0].ExternalId != jobId) throw new ArgumentException("Job is not uniquely available to the materials report. Synchronize or resolve the job first.");
        return await store.ChangeAsync(state => {
            if (state.Revision != revision) throw new ArgumentException("Purchase data changed. Refresh before assigning the group.");
            var key = HomeDepotHistoryText.Normalize(reference);
            if (key.Length == 0) throw new ArgumentException("Purchases without a reference may be unrelated. Assign them individually in transaction review.");
            if (!Rows(state).Any(x => Ref(x.Line) == key)) throw new ArgumentException("Source group not found.");
            var candidate = new HomeDepotCandidate(job.ExternalId, job.JobNumber, job.CustomerName, job.Address, job.Description,
                operationId, source.Operation.EffectiveDisplayName, "Explicit group review", job.CustomerId);
            return Task.FromResult(Save(state, key, candidate, actor, reason.Trim()));
        }, ct);
    }
}
