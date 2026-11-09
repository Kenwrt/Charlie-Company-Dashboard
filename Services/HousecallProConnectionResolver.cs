using System.Net.Http.Headers;
using System.Text.Json;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

// Deliberately not a record: generated record ToString must never expose credentials.
public sealed class HcpConnection(string apiKey, string? companyId)
{
    public string? CompanyId { get; } = companyId;
    public void Apply(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (CompanyId is not null) request.Headers.Add("X-Company-Id", CompanyId);
    }
    public override string ToString() => CompanyId is null ? "HCP local connection" : "HCP scoped CCVG connection";

    public void ValidateCompany(JsonElement root)
    {
        if (CompanyId is null) return;
        void Check(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("company_id", out var id)
                || id.ValueKind != JsonValueKind.String || id.GetString() != CompanyId)
                throw new InvalidDataException("HCP returned a record outside the requested company scope or without company identity.");
        }
        if (root.TryGetProperty("company_id", out _)) { Check(root); return; }
        foreach (var name in new[] { "jobs", "estimates", "invoices" })
            if (root.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray()) Check(item);
                return;
            }
        throw new InvalidDataException("HCP returned an unrecognized company-scoped response.");
    }

    // The official scoped invoice-list response omits company_id. The authenticated
    // X-Company-Id request supplies its context, including invoices for deleted jobs.
    // Check any identity the provider does supply; never infer it from invoice numbers.
    public void ValidateScopedInvoice(JsonElement invoice)
    {
        if (CompanyId is null) return;
        if (invoice.ValueKind != JsonValueKind.Object || !invoice.TryGetProperty("id", out var id)
            || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()))
            throw new InvalidDataException("HCP scoped response contains an invalid invoice.");
        if (invoice.TryGetProperty("company_id", out var company)
            && (company.ValueKind != JsonValueKind.String || company.GetString() != CompanyId))
            throw new InvalidDataException("HCP invoice has a conflicting company identity.");
    }

    public void ValidateScopedInvoiceList(JsonElement root)
    {
        if (CompanyId is null) return;
        if (root.TryGetProperty("company_id", out var company)
            && (company.ValueKind != JsonValueKind.String || company.GetString() != CompanyId))
            throw new InvalidDataException("HCP invoice list has a conflicting company identity.");
        if (!root.TryGetProperty("invoices", out var invoices) || invoices.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("HCP invoice list is missing its collection.");
        foreach (var invoice in invoices.EnumerateArray()) ValidateScopedInvoice(invoice);
    }

    // Job-specific history additionally verifies the parent job before the request.
    public void ValidateInvoiceForVerifiedJob(JsonElement invoice, string verifiedJobId)
    {
        if (CompanyId is null) return;
        if (string.IsNullOrWhiteSpace(verifiedJobId) || invoice.ValueKind != JsonValueKind.Object
            || !invoice.TryGetProperty("job_id", out var job) || job.ValueKind != JsonValueKind.String
            || job.GetString() != verifiedJobId)
            throw new InvalidDataException("HCP invoice does not belong to the verified job.");
        if (invoice.TryGetProperty("company_id", out var company)
            && (company.ValueKind != JsonValueKind.String || company.GetString() != CompanyId))
            throw new InvalidDataException("HCP invoice has a conflicting company identity.");
    }

    public void ValidateInvoicesForVerifiedJob(JsonElement root, string verifiedJobId)
    {
        if (CompanyId is null) return;
        if (string.IsNullOrWhiteSpace(verifiedJobId)) throw new InvalidDataException("HCP invoice history requires a verified parent job.");
        if (root.TryGetProperty("company_id", out var company)
            && (company.ValueKind != JsonValueKind.String || company.GetString() != CompanyId))
            throw new InvalidDataException("HCP invoice history has a conflicting company identity.");
        if (!root.TryGetProperty("invoices", out var invoices) || invoices.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("HCP invoice history is missing its collection.");
        foreach (var invoice in invoices.EnumerateArray())
        {
            // /jobs/{verifiedJobId}/invoices omits job_id as well as company_id.
            // Its URL provides the owning-job context, verified before this read.
            if (invoice.ValueKind != JsonValueKind.Object || !invoice.TryGetProperty("id", out var id)
                || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()))
                throw new InvalidDataException("HCP invoice history contains an invalid invoice.");
            if (invoice.TryGetProperty("job_id", out _)) ValidateInvoiceForVerifiedJob(invoice, verifiedJobId);
            else if (invoice.TryGetProperty("company_id", out var invoiceCompany)
                && (invoiceCompany.ValueKind != JsonValueKind.String || invoiceCompany.GetString() != CompanyId))
                throw new InvalidDataException("HCP invoice history contains a conflicting company identity.");
        }
    }
}

public sealed class HousecallProConnectionResolver(IServiceScopeFactory scopes, IOptions<HousecallProOptions> options)
{
    public async Task<HcpConnection?> ResolveAsync(string slug, CancellationToken ct = default)
    {
        var settings = options.Value;
        var key = settings.GetApiKey(slug);
        if (key is null) return null;
        if (!settings.Ccvg.UsesOperation(slug)) return new HcpConnection(key, null);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var operation = await db.LocalOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug && x.IsActive, ct);
        var companyId = operation?.HousecallProLocationId;
        if (string.IsNullOrWhiteSpace(companyId) || companyId.Length > 160 || companyId.Any(char.IsControl))
            throw new InvalidOperationException("The CCVG operation requires an active operation and an HCP location mapping.");
        return new HcpConnection(key, companyId);
    }
}
