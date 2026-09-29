using System.Text.Json;

namespace CharleyCompany.Dashboard.Web.Data;

// Planning snapshots use the existing append-only, per-job customization/event store.
// Payment records remain authoritative for collections; permit costs never become payments.
public sealed class JobStagePlan
{
    public string Stage { get; set; } = "Customer Estimate";
    public bool NotApplicable { get; set; }
    public DateOnly? Start { get; set; }
    public int? DurationDays { get; set; }
    public DateOnly? Completed { get; set; }
    public string? PreparationNotes { get; set; }
    public decimal CompanyPermitCost { get; set; }
    public DateOnly? CompanyCostDue { get; set; }
    public bool CompanyCostPaid { get; set; }
    public DateOnly? PaymentDue { get; set; }
    public int? ExtendedMilestoneId { get; set; }
    public DateOnly? End => Start is null ? null : Start.Value.AddDays(Math.Max(0, (DurationDays ?? 1) - 1));
}

public static class JobReviewWorkflow
{
    public const string EventType = "Job workflow stage v1";
    public static readonly string[] Stages = [
        "Customer Estimate", "Detailed Customer Estimate", "Schedule Job With Design Fee",
        "Schedule Job Without Design Fee", "Permit Preparation", "Permit Submitted to the City",
        "HOA Preparation", "HOA Submission", "Schedule Day One", "Order Materials", "Day One", "Demo",
        "Structural Framing Begins", "Waiting on Inspection", "May Extend Payment Period",
        "Structural Complete", "Finish Work Begins", "Schedule Final Inspection", "Schedule Final Walkthrough", "Complete"];
    public static bool IsOptional(string stage) => stage is "Permit Preparation" or "Permit Submitted to the City" or "May Extend Payment Period";
    public static Dictionary<string, JobStagePlan> Plans(IEnumerable<HousecallProJobProgressEvent> events)
    {
        var plans = new Dictionary<string, JobStagePlan>();
        foreach (var entry in events.Where(x => x.EventType == EventType).OrderBy(x => x.Id))
        {
            try
            {
                var plan = JsonSerializer.Deserialize<JobStagePlan>(entry.Details ?? "null");
                if (plan is not null && Stages.Contains(plan.Stage))
                {
                    if (plan.Stage == Stages[2]) plans.Remove(Stages[3]);
                    if (plan.Stage == Stages[3]) plans.Remove(Stages[2]);
                    plans[plan.Stage] = plan;
                }
            }
            catch (JsonException) { /* Preserve unrelated/older event payloads. */ }
        }
        return plans;
    }
    public static string Next(HousecallProJob job)
    {
        var current = job.Progress?.CurrentPhase ?? "";
        var index = Array.IndexOf(Stages, current);
        if (index < 0) return current switch
        {
            "Design" => "Permitting", "Permitting" => "HOA Approval", "HOA Approval" => "Framing Complete",
            "Framing Complete" => "Scheduled", "Scheduled" => "Day One", "Inspection" => "Final Payment",
            "Final Payment" => "Complete", _ => "Not scheduled"
        };
        var plans = Plans(job.ProgressEvents);
        var path = job.PaymentMilestones.FirstOrDefault(item => item.TriggerPhase == Stages[2] || item.TriggerPhase == Stages[3])?.TriggerPhase
            ?? (plans.ContainsKey(Stages[3]) ? Stages[3] : Stages[2]);
        return Stages.Skip(index + 1).FirstOrDefault(stage =>
            !(stage == Stages[2] && path == Stages[3])
            && !(stage == Stages[3] && path == Stages[2]) &&
            !(current == "Schedule Job With Design Fee" && stage == "Schedule Job Without Design Fee")
            && !(plans.TryGetValue(stage, out var plan) && plan.NotApplicable)) ?? "Complete";
    }
    public static IReadOnlyList<HousecallProJobPaymentMilestone> Payments(HousecallProJob job, bool design)
    {
        var price = decimal.Round(job.JobPrice, 2, MidpointRounding.AwayFromZero);
        if (price <= 0) throw new InvalidOperationException("A positive job price is required.");
        var fee = design ? Math.Min(1500m, price) : 0m;
        // Preserve the existing convention: credit design first, then allocate the remaining contract.
        var remaining = price - fee;
        var thirty = decimal.Round(remaining * .30m, 2, MidpointRounding.AwayFromZero);
        var second = Math.Min(thirty, remaining - thirty);
        var third = Math.Min(thirty, remaining - thirty - second);
        var final = remaining - thirty - second - third;
        var phases = new[] { design ? Stages[2] : Stages[3], "Schedule Day One", "Day One", "Structural Complete", "Schedule Final Walkthrough" };
        var amounts = new[] { fee, thirty, second, third, final };
        var plans = Plans(job.ProgressEvents);
        return phases.Select((phase, i) => new HousecallProJobPaymentMilestone
        {
            HousecallProJobId = job.Id, Name = phase, TriggerPhase = phase, Amount = amounts[i],
            ExpectedPaymentDate = plans.TryGetValue(phase, out var plan) ? plan.PaymentDue ?? plan.Start : null,
            Status = "Pending", Notes = "Design payment is included in the job price. Remaining balance allocated 30/30/30/10; final milestone absorbs rounding."
        }).ToArray();
    }
}
