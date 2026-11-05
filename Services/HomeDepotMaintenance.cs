using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CharleyCompany.Dashboard.Web.Services;

/// <summary>Explicit local-owner intake command. Never binds HTTP, assigns jobs, or writes payables.</summary>
public static class HomeDepotMaintenance
{
    public static async Task ImportAsync(IServiceProvider services, string path)
    {
        var history = services.GetRequiredService<HomeDepotHistoryService>();
        var bytes = await File.ReadAllBytesAsync(path);
        await history.ImportCsvAsync(bytes, "maintenance:owner-authorized-import", CancellationToken.None);
        var store = services.GetRequiredService<HomeDepotHistoryStore>();
        var factory = services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var operations = await db.LocalOperations.AsNoTracking().Where(o => o.IsActive).ToListAsync();
        var hcp = services.GetRequiredService<HousecallProDataService>();
        var jobs = new List<(LocalOperation Operation, List<HcpSupplierJob> Jobs)>();
        var failures = 0;
        foreach (var operation in operations)
        {
            try { jobs.Add((operation, (await hcp.GetSupplierJobsAsync(operation.Slug, CancellationToken.None)).ToList())); }
            catch { failures++; }
        }
        var counts = await store.ChangeAsync(state => {
            var candidates = 0;
            foreach (var purchase in state.Purchases)
            {
                var references = purchase.ReportingLines.SelectMany(l => new[] { l.Value("Job Name"), l.Value("Project Name") }).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToList();
                purchase.Review.Candidates = jobs.SelectMany(j => HomeDepotJobMatcher.Find(references, j.Jobs, j.Operation.Id, j.Operation.EffectiveDisplayName)).OrderByDescending(c => c.Rank).ToList();
                candidates += purchase.Review.Candidates.Count > 0 ? 1 : 0;
                purchase.Review.LookupAt = DateTimeOffset.UtcNow;
                purchase.Review.LookupStatus = failures > 0 ? "IncompleteOperationCoverage" : references.Count == 0 ? "ReferenceRequired" : purchase.Review.Candidates.Count > 0 ? "CandidatesRequireReview" : "CompleteNoCandidate";
                purchase.Review.LookupMessage = "Read-only proposals across active operations; confirm operation and job before allocating. No automatic assignments.";
                purchase.Version++;
            }
            state.Audit.Add(new(DateTimeOffset.UtcNow, "maintenance:owner-authorized-import", "BatchJobProposals", null, "", "", "Operation-scoped API candidates only; no assignments, catalog changes or payables."));
            return Task.FromResult(new { documents = state.Documents.Count, transactions = state.Purchases.Count, lines = state.Purchases.Sum(p => p.ReportingLines.Count()), transactionsWithCandidates = candidates, incompleteOperations = failures, createsAccountsPayable = false });
        }, CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(counts));
    }
}
