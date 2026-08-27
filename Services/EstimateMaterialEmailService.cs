using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class EstimateMaterialEmailService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IOptions<EmailOptions> options,
    ResendEmailClient resend,
    ILogger<EstimateMaterialEmailService> logger)
{
    private readonly EmailOptions settings = options.Value;

    public async Task<EstimateMaterialEmailResult> SendAsync(
        int quoteId,
        string recipient,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var quote = await db.QuoteCases.AsSplitQuery()
            .Include(item => item.LocalOperation)
            .Include(item => item.ProjectTasks).ThenInclude(item => item.Analyses).ThenInclude(item => item.Materials)
                .ThenInclude(item => item.VendorProduct).ThenInclude(item => item!.SupplyVendor)
            .SingleOrDefaultAsync(item => item.Id == quoteId, cancellationToken)
            ?? throw new InvalidOperationException("The saved estimate could not be loaded for material email delivery.");

        var lines = quote.ProjectTasks
            .Where(task => !task.IsDeleted)
            .OrderBy(task => task.SortOrder)
            .Select(task => new
            {
                Task = task,
                Analysis = task.Analyses
                    .Where(analysis => analysis.Status is QuoteTaskAnalysisStatuses.Accepted or QuoteTaskAnalysisStatuses.NeedsReview)
                    .OrderByDescending(analysis => analysis.RevisionNumber)
                    .FirstOrDefault()
            })
            .Where(item => item.Analysis is not null)
            .SelectMany(item => item.Analysis!.Materials
                .Where(material => !material.IsRemoved && material.Quantity > 0)
                .Select(material => new MaterialEmailLine(item.Task, material, VendorName(material))))
            .ToList();

        var materialSignature = MaterialSignature(lines);
        if (string.Equals(quote.LastMaterialEmailSignature, materialSignature, StringComparison.Ordinal))
            return new EstimateMaterialEmailResult(0, [], false);

        if (lines.Count == 0)
        {
            quote.LastMaterialEmailSignature = materialSignature;
            quote.LastMaterialEmailSentAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return new EstimateMaterialEmailResult(0, [], true);
        }

        if (string.IsNullOrWhiteSpace(recipient))
            throw new InvalidOperationException("The logged-in user does not have an email address.");

        var estimateNumber = quote.HousecallProEstimateNumber
            ?? quote.HousecallProQuoteId
            ?? $"CCV-E-{quote.Id:D6}";
        var vendors = new List<string>();
        foreach (var group in lines.GroupBy(item => item.VendorName).OrderBy(item => item.Key))
        {
            var subject = $"Estimate #: {estimateNumber} {group.Key} mateirals list";
            var html = BuildHtml(quote, estimateNumber, group.Key, group.ToList());
            var text = BuildText(quote, estimateNumber, group.Key, group.ToList());
            await SendEmailAsync(recipient, subject, html, text, cancellationToken);
            vendors.Add(group.Key);
            logger.LogInformation(
                "Estimate {QuoteId} material list for {VendorName} emailed to {Recipient}.",
                quoteId, group.Key, recipient);
        }

        quote.LastMaterialEmailSignature = materialSignature;
        quote.LastMaterialEmailSentAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new EstimateMaterialEmailResult(vendors.Count, vendors, true);
    }

    private async Task SendEmailAsync(
        string recipient,
        string subject,
        string html,
        string text,
        CancellationToken cancellationToken)
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
        if (!string.IsNullOrWhiteSpace(settings.ReplyToAddress))
            message.ReplyToList.Add(settings.ReplyToAddress);

        using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort) { EnableSsl = settings.UseSsl };
        if (!string.IsNullOrWhiteSpace(settings.Username))
            client.Credentials = new NetworkCredential(settings.Username, settings.Password);
        await client.SendMailAsync(message, cancellationToken);
    }

    private static string BuildHtml(
        QuoteCase quote,
        string estimateNumber,
        string vendorName,
        IReadOnlyCollection<MaterialEmailLine> lines)
    {
        var rows = new StringBuilder();
        foreach (var line in lines.OrderBy(item => item.Task.SortOrder).ThenBy(item => item.Material.SortOrder))
        {
            rows.Append("<tr>")
                .Append(Cell($"Task {line.Task.SortOrder} — {line.Task.TaskType}"))
                .Append(Cell(line.Material.VendorSku ?? "No catalog SKU"))
                .Append(Cell(line.Material.Description))
                .Append(Cell($"{line.Material.Quantity:N2} {line.Material.Unit}"))
                .Append(Cell($"{line.Material.WastePercent:N1}%"))
                .Append(Cell(line.Material.UnitCost.ToString("C2")))
                .Append(Cell(line.Material.ExtendedCost.ToString("C2")))
                .Append("</tr>");
        }

        var total = lines.Sum(item => item.Material.ExtendedCost);
        return $$"""
            <!doctype html>
            <html><body style="margin:0;background:#f4f7fb;font-family:Arial,sans-serif;color:#172033">
              <div style="max-width:900px;margin:24px auto;background:#fff;border:1px solid #dbe3ef;border-radius:12px;overflow:hidden">
                <div style="background:#0b2559;color:#fff;padding:24px 28px">
                  <div style="font-size:12px;text-transform:uppercase;letter-spacing:1.2px;opacity:.8">Charlie Company Ventures</div>
                  <h1 style="margin:8px 0 4px;font-size:24px">{{Html(vendorName)}} Materials List</h1>
                  <div>Estimate #{{Html(estimateNumber)}} · {{Html(quote.LocalOperation.EffectiveDisplayName)}}</div>
                </div>
                <div style="padding:24px 28px">
                  <p style="margin-top:0"><strong>Customer:</strong> {{Html(quote.CustomerName ?? "Not provided")}}<br>
                  <strong>Project address:</strong> {{Html(quote.CustomerAddress ?? "Not provided")}}</p>
                  <table style="width:100%;border-collapse:collapse;font-size:14px">
                    <thead><tr style="background:#eef3fa;text-align:left">
                      <th style="padding:10px;border-bottom:2px solid #cbd6e5">Project task</th>
                      <th style="padding:10px;border-bottom:2px solid #cbd6e5">SKU</th>
                      <th style="padding:10px;border-bottom:2px solid #cbd6e5">Material</th>
                      <th style="padding:10px;border-bottom:2px solid #cbd6e5">Quantity</th>
                      <th style="padding:10px;border-bottom:2px solid #cbd6e5">Waste</th>
                      <th style="padding:10px;border-bottom:2px solid #cbd6e5">Unit cost</th>
                      <th style="padding:10px;border-bottom:2px solid #cbd6e5">Extended</th>
                    </tr></thead>
                    <tbody>{{rows}}</tbody>
                    <tfoot><tr><td colspan="6" style="padding:12px 10px;text-align:right;font-weight:bold">Vendor material subtotal</td><td style="padding:12px 10px;font-weight:bold">{{total:C2}}</td></tr></tfoot>
                  </table>
                  <p style="margin-bottom:0;margin-top:20px;color:#5e6b82;font-size:12px">Generated when the estimate was saved. Verify quantities, availability, and current vendor pricing before ordering.</p>
                </div>
              </div>
            </body></html>
            """;
    }

    private static string BuildText(QuoteCase quote, string estimateNumber, string vendorName, IReadOnlyCollection<MaterialEmailLine> lines)
    {
        var text = new StringBuilder()
            .AppendLine($"{vendorName} Materials List")
            .AppendLine($"Estimate #: {estimateNumber}")
            .AppendLine($"Customer: {quote.CustomerName ?? "Not provided"}")
            .AppendLine($"Project address: {quote.CustomerAddress ?? "Not provided"}")
            .AppendLine();
        foreach (var line in lines.OrderBy(item => item.Task.SortOrder).ThenBy(item => item.Material.SortOrder))
            text.AppendLine($"Task {line.Task.SortOrder} - {line.Task.TaskType} | {line.Material.VendorSku ?? "No catalog SKU"} | {line.Material.Description} | {line.Material.Quantity:N2} {line.Material.Unit} | Waste {line.Material.WastePercent:N1}% | {line.Material.ExtendedCost:C2}");
        text.AppendLine().AppendLine($"Vendor material subtotal: {lines.Sum(item => item.Material.ExtendedCost):C2}");
        return text.ToString();
    }

    private static string VendorName(QuoteTaskAnalysisMaterial material)
    {
        if (!string.IsNullOrWhiteSpace(material.VendorProduct?.SupplyVendor?.Name))
            return material.VendorProduct.SupplyVendor.Name;
        if (material.SourceType?.Contains("Home Depot", StringComparison.OrdinalIgnoreCase) == true)
            return "Home Depot";
        if (!string.IsNullOrWhiteSpace(material.SourceType)
            && !material.SourceType.Contains("CentCom", StringComparison.OrdinalIgnoreCase)
            && !material.SourceType.Contains("Manual", StringComparison.OrdinalIgnoreCase))
            return material.SourceType;
        return "Unresolved Materials";
    }

    private static string MaterialSignature(IEnumerable<MaterialEmailLine> lines)
    {
        var canonical = string.Join("\n", lines
            .OrderBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Task.SortOrder)
            .ThenBy(item => item.Material.SortOrder)
            .Select(item => string.Join("|",
                item.VendorName.Trim().ToUpperInvariant(),
                item.Task.Id,
                item.Task.SortOrder,
                item.Material.VendorProductId,
                item.Material.VendorSku?.Trim().ToUpperInvariant(),
                item.Material.Description.Trim().ToUpperInvariant(),
                item.Material.Quantity.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
                item.Material.Unit.Trim().ToUpperInvariant(),
                item.Material.UnitCost.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
                item.Material.WastePercent.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture))));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string Cell(string value) => $"<td style=\"padding:10px;border-bottom:1px solid #e3e8f0;vertical-align:top\">{Html(value)}</td>";
    private static string Html(string value) => WebUtility.HtmlEncode(value);

    private sealed record MaterialEmailLine(QuoteProjectTask Task, QuoteTaskAnalysisMaterial Material, string VendorName);
}

public sealed record EstimateMaterialEmailResult(int EmailCount, IReadOnlyList<string> Vendors, bool MaterialListChanged);
