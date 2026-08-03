using System.Net;
using System.Net.Mail;
using System.Text;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class EstimateAdminAuditEmailService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> userManager,
    IOptions<EmailOptions> options,
    ResendEmailClient resend,
    ILogger<EstimateAdminAuditEmailService> logger)
{
    private readonly EmailOptions settings = options.Value;

    public async Task<EstimateAdminAuditEmailResult> SendInitialCompletionAsync(
        int quoteId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var quote = await db.QuoteCases.AsSplitQuery()
            .Include(item => item.LocalOperation)
            .Include(item => item.AuditEvents)
            .Include(item => item.Versions).ThenInclude(item => item.CostSnapshots).ThenInclude(item => item.CostingPolicyVersion).ThenInclude(item => item.Rules)
            .Include(item => item.Versions).ThenInclude(item => item.CostSnapshots).ThenInclude(item => item.CostingPolicyVersion).ThenInclude(item => item.CrewRates)
            .Include(item => item.Versions).ThenInclude(item => item.CostSnapshots).ThenInclude(item => item.CostingPolicyVersion).ThenInclude(item => item.MarginRules)
            .Include(item => item.Versions).ThenInclude(item => item.CostSnapshots).ThenInclude(item => item.Tasks).ThenInclude(item => item.QuoteProjectTask)
            .Include(item => item.Versions).ThenInclude(item => item.CostSnapshots).ThenInclude(item => item.Tasks).ThenInclude(item => item.RequiredSupplies)
            .Include(item => item.ProjectTasks).ThenInclude(item => item.Analyses).ThenInclude(item => item.Materials).ThenInclude(item => item.VendorProduct).ThenInclude(item => item!.SupplyVendor)
            .Include(item => item.ProjectTasks).ThenInclude(item => item.Analyses).ThenInclude(item => item.Exclusions)
            .Include(item => item.ProjectTasks).ThenInclude(item => item.Analyses).ThenInclude(item => item.ReviewItems)
            .SingleOrDefaultAsync(item => item.Id == quoteId, cancellationToken)
            ?? throw new InvalidOperationException("The completed estimate could not be loaded for the administrator audit email.");

        if (quote.AdminCompletionEmailSentAt is not null)
            return new EstimateAdminAuditEmailResult(0, false, []);

        var version = quote.Versions.OrderByDescending(item => item.VersionNumber).FirstOrDefault();
        var snapshot = version?.CostSnapshots.OrderByDescending(item => item.RevisionNumber).FirstOrDefault();
        if (version is null || snapshot is null)
            return new EstimateAdminAuditEmailResult(0, false, []);

        var administrators = await userManager.GetUsersInRoleAsync(ApplicationRoles.Administrator);
        var recipients = administrators
            .Where(item => item.AdminAuditEmail)
            .Select(item => item.Email)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item)
            .ToList();
        if (recipients.Count == 0)
            throw new InvalidOperationException("No Administrator accounts are enabled to receive audit emails and have an email address configured.");

        var estimateNumber = quote.HousecallProEstimateNumber ?? quote.HousecallProQuoteId ?? $"CCV-E-{quote.Id:D6}";
        var subject = $"Estimate #: {estimateNumber} administrative audit and costing report";
        var html = BuildHtml(quote, version, snapshot, estimateNumber);
        var text = BuildText(quote, version, snapshot, estimateNumber);
        foreach (var recipient in recipients)
        {
            await SendEmailAsync(recipient, subject, html, text, cancellationToken);
            logger.LogInformation("Initial administrator audit email for estimate {QuoteId} sent to {Recipient}.", quoteId, recipient);
        }

        quote.AdminCompletionEmailSentAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new EstimateAdminAuditEmailResult(recipients.Count, true, recipients);
    }

    private async Task SendEmailAsync(string recipient, string subject, string html, string text, CancellationToken cancellationToken)
    {
        if (settings.Provider.Equals("Resend", StringComparison.OrdinalIgnoreCase))
        {
            await resend.SendAsync(recipient, subject, html, text, cancellationToken);
            return;
        }
        if (string.IsNullOrWhiteSpace(settings.SmtpHost) || string.IsNullOrWhiteSpace(settings.FromAddress))
            throw new InvalidOperationException("Email service is not configured. Set Email:SmtpHost and Email:FromAddress.");

        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = subject,
            Body = html,
            IsBodyHtml = true
        };
        message.To.Add(recipient);
        if (!string.IsNullOrWhiteSpace(settings.ReplyToAddress)) message.ReplyToList.Add(settings.ReplyToAddress);
        using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort) { EnableSsl = settings.UseSsl };
        if (!string.IsNullOrWhiteSpace(settings.Username)) client.Credentials = new NetworkCredential(settings.Username, settings.Password);
        await client.SendMailAsync(message, cancellationToken);
    }

    private static string BuildHtml(QuoteCase quote, QuoteVersion version, QuoteCostSnapshot snapshot, string estimateNumber)
    {
        var policy = snapshot.CostingPolicyVersion;
        var latestAnalyses = quote.ProjectTasks.Where(task => !task.IsDeleted)
            .Select(task => new TaskAnalysisEntry(task, task.Analyses.OrderByDescending(item => item.RevisionNumber).FirstOrDefault()))
            .Where(item => item.Analysis is not null).ToList();
        var pricingAfterDiscount = Math.Max(0, snapshot.SuggestedCustomerPrice - version.DiscountAmount);
        var tax = decimal.Round(pricingAfterDiscount * version.TaxRate / 100m, 2);
        var customerTotal = pricingAfterDiscount + tax;

        var html = new StringBuilder();
        html.Append("<!doctype html><html><body style=\"margin:0;background:#eef2f7;font-family:Arial,sans-serif;color:#172033\"><div style=\"max-width:1100px;margin:24px auto;background:#fff;border:1px solid #d7e0ec;border-radius:12px;overflow:hidden\">")
            .Append("<div style=\"background:#0b2559;color:#fff;padding:26px 30px\"><div style=\"font-size:12px;text-transform:uppercase;letter-spacing:1.2px;opacity:.8\">Administrator hard copy · Charlie Company Ventures</div>")
            .Append($"<h1 style=\"margin:8px 0 4px;font-size:26px\">Estimate #{H(estimateNumber)} Audit &amp; Costing Report</h1>")
            .Append($"<div>{H(quote.LocalOperation.Name)} · Generated {DateTimeOffset.Now:MM/dd/yyyy h:mm tt zzz}</div></div><div style=\"padding:26px 30px\">")
            .Append(Section("1. Estimate and customer", KeyValues([
                ("CCV estimate", $"CCV-E-{quote.Id:D6}"), ("Housecall Pro estimate", estimateNumber), ("Status", quote.Status),
                ("Customer", quote.CustomerName ?? "Not provided"), ("Customer email", quote.CustomerEmail ?? "Not provided"),
                ("Project address", quote.CustomerAddress ?? "Not provided"), ("Project overview", quote.WorkDescription),
                ("Version", version.VersionNumber.ToString()), ("Cost snapshot", $"Revision {snapshot.RevisionNumber} priced {snapshot.PricedAt.ToLocalTime():MM/dd/yyyy h:mm tt}")
            ])))
            .Append(Section("2. Internal job-cost and customer-price summary", KeyValues([
                ("Direct cost", snapshot.DirectCost.ToString("C2")), ("Contingency", snapshot.Contingency.ToString("C2")),
                ("Project overhead", snapshot.ProjectOverhead.ToString("C2")), ("Fully burdened job cost", snapshot.TotalCost.ToString("C2")),
                ("Target gross margin", $"{snapshot.TargetMarginPercent:N2}%"), ("Price before discount", snapshot.SuggestedCustomerPrice.ToString("C2")),
                ("Promotional discount", version.DiscountAmount.ToString("C2")), ("Tax rate", $"{version.TaxRate:N2}%"),
                ("Tax", tax.ToString("C2")), ("Customer total", customerTotal.ToString("C2")),
                ("Effective margin after discount", pricingAfterDiscount <= 0 ? "0.00%" : $"{(pricingAfterDiscount - snapshot.TotalCost) / pricingAfterDiscount * 100m:N2}%"),
                ("Pricing adjustment reason", snapshot.AdjustmentReason ?? "None recorded")
            ])))
            .Append(Section("3. Applied costing policy", PolicyHtml(policy)))
            .Append(Section("4. Task-level cost breakdown", TaskCostsHtml(snapshot)))
            .Append(Section("5. Detailed material resolution", MaterialsHtml(latestAnalyses)))
            .Append(Section("6. Assumptions, questions, warnings, and exclusions", ReviewHtml(latestAnalyses)))
            .Append(Section("7. Estimate audit history", AuditHtml(quote.AuditEvents)))
            .Append("<p style=\"margin:24px 0 0;color:#64748b;font-size:12px\">This administrative report is an immutable email copy of the pricing and audit data available when the estimate was initially completed. Verify vendor availability and pricing before ordering.</p></div></div></body></html>");
        return html.ToString();
    }

    private static string PolicyHtml(CostingPolicyVersion policy)
    {
        var result = new StringBuilder(KeyValues([
            ("Policy", $"{policy.Name} revision {policy.RevisionNumber}"), ("Effective", policy.EffectiveDate.ToString("MM/dd/yyyy")),
            ("Default daily crew cost", policy.DefaultDailyCrewCost.ToString("C2")), ("Default crew size", policy.DefaultCrewSize.ToString("N2")),
            ("Default contingency", $"{policy.DefaultContingencyPercent:N2}%"), ("Default target margin", $"{policy.DefaultTargetMarginPercent:N2}%"),
            ("General overhead fixed", policy.GeneralOverheadFixed.ToString("C2")), ("General overhead per project day", policy.GeneralOverheadPerProjectDay.ToString("C2")),
            ("General overhead percent", $"{policy.GeneralOverheadPercent:N2}%"), ("Calculated overhead per crew day", policy.CalculatedOverheadPerCrewDay.ToString("C2"))
        ]));
        result.Append(Table(["Policy rule", "Scope", "Task type", "Calculation", "Rate", "Status"], policy.Rules.OrderBy(item => item.Name).Select(item => new[] { item.Name, item.Scope, item.TaskType ?? "All", item.CalculationMethod, item.Rate.ToString("N4"), item.IsActive ? "Active" : "Inactive" })));
        result.Append(Table(["Crew-rate task", "Work type", "Crew size", "Daily crew cost", "Status"], policy.CrewRates.OrderBy(item => item.TaskType).Select(item => new[] { item.TaskType, item.WorkType ?? "All", item.CrewSize.ToString("N2"), item.DailyCrewCost.ToString("C2"), item.IsActive ? "Active" : "Inactive" })));
        result.Append(Table(["Margin task", "Work type", "Target margin", "Status"], policy.MarginRules.OrderBy(item => item.TaskType).Select(item => new[] { item.TaskType, item.WorkType ?? "All", $"{item.TargetMarginPercent:N2}%", item.IsActive ? "Active" : "Inactive" })));
        return result.ToString();
    }

    private static string TaskCostsHtml(QuoteCostSnapshot snapshot) => Table(
        ["Task", "Materials", "Supply kits", "Labor", "Task costs", "Contingency", "Allocated overhead", "Total cost", "Margin", "Suggested price", "Applied rules"],
        snapshot.Tasks.OrderBy(item => item.QuoteProjectTask.SortOrder).Select(item => new[] {
            $"Task {item.QuoteProjectTask.SortOrder} — {item.QuoteProjectTask.TaskType} ({item.QuoteProjectTask.WorkType ?? "Unclassified"})",
            item.MaterialCost.ToString("C2"), item.RequiredSupplyCost.ToString("C2"), item.LaborCost.ToString("C2"), item.TaskOverhead.ToString("C2"),
            item.Contingency.ToString("C2"), item.AllocatedProjectOverhead.ToString("C2"), item.TotalCost.ToString("C2"),
            $"{item.TargetMarginPercent:N2}%", item.SuggestedCustomerPrice.ToString("C2"), item.AppliedRules ?? "Policy defaults"
        }));

    private static string MaterialsHtml(IEnumerable<TaskAnalysisEntry> taskAnalyses)
    {
        var rows = new List<string[]>();
        foreach (var entry in taskAnalyses)
        foreach (QuoteTaskAnalysisMaterial item in entry.Analysis!.Materials.OrderBy(x => x.SortOrder))
            rows.Add([$"Task {entry.Task.SortOrder} — {entry.Task.TaskType}", item.VendorProduct?.SupplyVendor?.Name ?? item.SourceType ?? "Unresolved", item.VendorSku ?? "No SKU", item.Description,
                $"{item.Quantity:N2} {item.Unit}", $"{item.WastePercent:N2}%", item.UnitCost.ToString("C2"), item.ExtendedCost.ToString("C2"), item.MatchKind, $"{item.MatchConfidence:P0} / {item.ReviewDecision}", item.IsRemoved ? "Removed" : "Included"]);
        return Table(["Task", "Vendor/source", "SKU", "Material", "Quantity", "Waste", "Unit cost", "Extended", "Match", "Confidence/review", "Estimate status"], rows);
    }

    private static string ReviewHtml(IEnumerable<TaskAnalysisEntry> taskAnalyses)
    {
        var rows = new List<string[]>();
        foreach (var entry in taskAnalyses)
        {
            foreach (QuoteTaskAnalysisReviewItem item in entry.Analysis!.ReviewItems.OrderBy(x => x.ReviewKind).ThenBy(x => x.SortOrder))
                rows.Add([$"Task {entry.Task.SortOrder} — {entry.Task.TaskType}", item.ReviewKind, item.Category, item.Description, item.Status, item.EstimatorResponse ?? "No response", item.ResolutionAction ?? "No action", item.AdditionalFeeAmount.ToString("C2"), item.ResolvedAt?.ToLocalTime().ToString("MM/dd/yyyy h:mm tt") ?? "Open"]);
            foreach (QuoteTaskAnalysisExclusion item in entry.Analysis.Exclusions.OrderBy(x => x.Description))
                rows.Add([$"Task {entry.Task.SortOrder} — {entry.Task.TaskType}", "Policy exclusion", "Excluded material", item.Description, "Excluded", item.Reason, "Costing-policy recovery", "$0.00", item.ExcludedAt.ToLocalTime().ToString("MM/dd/yyyy h:mm tt")]);
        }
        return Table(["Task", "Type", "Category", "Description", "Status", "Estimator response/reason", "Resolution", "Fee", "Resolved"], rows);
    }

    private static string AuditHtml(IEnumerable<QuoteAuditEvent> events) => Table(
        ["Date/time", "Status change", "User ID", "Audit explanation"],
        events.OrderBy(item => item.OccurredAt).Select(item => new[] { item.OccurredAt.ToLocalTime().ToString("MM/dd/yyyy h:mm:ss tt"), $"{item.PreviousStatus} → {item.NewStatus}", item.UserId ?? "System", item.Explanation ?? "No explanation" }));

    private static string BuildText(QuoteCase quote, QuoteVersion version, QuoteCostSnapshot snapshot, string estimateNumber) =>
        $"Estimate #{estimateNumber} administrative audit and costing report\nCustomer: {quote.CustomerName}\nAddress: {quote.CustomerAddress}\nPolicy: {snapshot.CostingPolicyVersion.Name} r{snapshot.CostingPolicyVersion.RevisionNumber}\nDirect cost: {snapshot.DirectCost:C2}\nContingency: {snapshot.Contingency:C2}\nProject overhead: {snapshot.ProjectOverhead:C2}\nFully burdened cost: {snapshot.TotalCost:C2}\nTarget margin: {snapshot.TargetMarginPercent:N2}%\nPrice before discount: {snapshot.SuggestedCustomerPrice:C2}\nDiscount: {version.DiscountAmount:C2}\nTax rate: {version.TaxRate:N2}%\n\nThe HTML version of this email contains the detailed task, policy, material-resolution, review, and audit tables.";

    private static string Section(string title, string body) => $"<section style=\"margin-top:24px\"><h2 style=\"margin:0 0 12px;padding-bottom:7px;border-bottom:2px solid #1d4ed8;font-size:19px\">{H(title)}</h2>{body}</section>";
    private static string KeyValues(IEnumerable<(string Key, string Value)> items) => $"<table style=\"width:100%;border-collapse:collapse\">{string.Join("", items.Select(item => $"<tr><th style=\"width:28%;padding:7px 9px;border-bottom:1px solid #e2e8f0;text-align:left;background:#f8fafc\">{H(item.Key)}</th><td style=\"padding:7px 9px;border-bottom:1px solid #e2e8f0\">{H(item.Value)}</td></tr>"))}</table>";
    private static string Table(IEnumerable<string> headers, IEnumerable<string[]> rows)
    {
        var header = string.Join("", headers.Select(item => $"<th style=\"padding:8px;border:1px solid #dbe3ef;background:#eef3fa;text-align:left;font-size:12px\">{H(item)}</th>"));
        var body = string.Join("", rows.Select(row => $"<tr>{string.Join("", row.Select(item => $"<td style=\"padding:8px;border:1px solid #e2e8f0;vertical-align:top;font-size:12px\">{H(item)}</td>"))}</tr>"));
        return $"<div style=\"overflow-x:auto\"><table style=\"width:100%;border-collapse:collapse\"><thead><tr>{header}</tr></thead><tbody>{body}</tbody></table></div>";
    }
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private sealed record TaskAnalysisEntry(QuoteProjectTask Task, QuoteTaskAnalysis? Analysis);
}

public sealed record EstimateAdminAuditEmailResult(int EmailCount, bool Sent, IReadOnlyList<string> Recipients);
