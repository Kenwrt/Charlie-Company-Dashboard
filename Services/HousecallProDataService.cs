using System.Net.Http.Headers;
using System.Text.Json;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed record HcpImportedCommunication(string Type, string Notes, string EnteredBy, DateTimeOffset? EnteredAt);
public sealed record HcpJobPayment(
    string InvoiceNumber,
    string Type,
    string Status,
    string Method,
    decimal Amount,
    string? Note,
    string? Category,
    DateTimeOffset? PaidAt);
public sealed record HcpJobPaymentHistory(IReadOnlyList<HcpJobPayment> Transactions);
public sealed record HcpRecordDetail(
    string Kind,
    string ExternalId,
    IReadOnlyList<KeyValuePair<string, string>> Fields,
    IReadOnlyList<HcpImportedCommunication> Communications,
    string Json);
public sealed record HcpEstimateStartRecord(
    string ExternalId,
    string EstimateNumber,
    string CustomerName,
    string? CustomerEmail,
    string? CustomerPhone,
    string CustomerAddress,
    DateTimeOffset? EstimateDate);

public sealed class HousecallProDataService(
    HttpClient httpClient,
    IServiceScopeFactory scopeFactory,
    IOptions<HousecallProOptions> options,
    IMemoryCache cache,
    ILogger<HousecallProDataService> logger)
{
    private readonly HousecallProOptions settings = options.Value;

    public async Task SyncAllConfiguredOperationsAsync(CancellationToken cancellationToken = default)
    {
        await using var readScope = scopeFactory.CreateAsyncScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var operations = await readDb.LocalOperations.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        foreach (var operation in operations)
        {
            var apiKey = settings.GetApiKey(operation.Slug);
            if (apiKey is null) continue;
            try { await SyncOperationAsync(operation, apiKey, cancellationToken); }
            catch (Exception ex) { logger.LogError(ex, "Housecall Pro record sync failed for {OperationSlug}.", operation.Slug); }
        }
    }

    public async Task SyncOperationAsync(LocalOperation operation, string apiKey, CancellationToken cancellationToken = default)
    {
        await using var dataScope = scopeFactory.CreateAsyncScope();
        var dbContext = dataScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // The Jobs page is the operational system of record, so its synchronized cache must
        // include older unscheduled work as well as the most recent scheduled jobs.
        var jobs = await GetPagesAsync(
            settings.JobsEndpoint,
            "jobs",
            apiKey,
            cancellationToken,
            maxPages: int.MaxValue);
        var estimates = await GetPagesAsync(
            settings.EstimatesEndpoint,
            "estimates",
            apiKey,
            cancellationToken,
            maxPages: int.MaxValue);
        var syncedAt = DateTimeOffset.UtcNow;
        var existingJobs = await dbContext.HousecallProJobs
            .Where(x => x.LocalOperationId == operation.Id)
            .ToDictionaryAsync(x => x.ExternalId, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var existingEstimates = await dbContext.HousecallProEstimates
            .Where(x => x.LocalOperationId == operation.Id)
            .ToDictionaryAsync(x => x.ExternalId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var item in jobs)
        {
            var externalId = Text(item, "id");
            if (externalId is null) continue;
            if (!existingJobs.TryGetValue(externalId, out var record))
            {
                record = new HousecallProJob { LocalOperationId = operation.Id, ExternalId = externalId };
                dbContext.HousecallProJobs.Add(record);
                existingJobs[externalId] = record;
            }
            record.JobNumber = Text(item, "invoice_number") ?? Text(item, "job_number");
            record.CustomerName = CustomerName(item);
            record.CreatedByName = CreatorName(item) ?? record.CreatedByName;
            record.CustomerEmail = CustomerValue(item, "email");
            record.CustomerPhone = FirstNonEmpty(
                CustomerValue(item, "mobile_number"),
                CustomerValue(item, "phone_number"),
                CustomerValue(item, "home_number"),
                CustomerValue(item, "work_number"));
            record.WorkStatus = Text(item, "work_status");
            record.Address = CustomerAddress(item);
            record.ScheduledStart = NestedDate(item, "schedule", "scheduled_start");
            record.ScheduledEnd = NestedDate(item, "schedule", "scheduled_end");
            var subtotal = Money(item, "subtotal");
            record.JobPrice = subtotal > 0 ? subtotal : Money(item, "total_amount");
            record.TotalAmount = Money(item, "total_amount");
            record.OutstandingBalance = Money(item, "outstanding_balance");
            record.SourceUpdatedAt = Date(item, "updated_at");
            record.LastSyncedAt = syncedAt;
        }

        foreach (var item in estimates)
        {
            var externalId = Text(item, "id");
            if (externalId is null) continue;
            if (!existingEstimates.TryGetValue(externalId, out var record))
            {
                record = new HousecallProEstimate { LocalOperationId = operation.Id, ExternalId = externalId };
                dbContext.HousecallProEstimates.Add(record);
                existingEstimates[externalId] = record;
            }
            record.EstimateNumber = Text(item, "estimate_number") ?? Text(item, "number");
            record.CustomerName = CustomerName(item);
            record.CreatedByName = CreatorName(item) ?? record.CreatedByName;
            record.CustomerEmail = CustomerValue(item, "email");
            record.CustomerPhone = FirstNonEmpty(
                CustomerValue(item, "mobile_number"),
                CustomerValue(item, "phone_number"),
                CustomerValue(item, "home_number"),
                CustomerValue(item, "work_number"));
            record.CustomerAddress = CustomerAddress(item);
            record.Status = Text(item, "status") ?? FirstOptionText(item, "status");
            record.ApprovalStatus = FirstOptionText(item, "approval_status");
            record.EstimateDate = Date(item, "created_at") ?? FirstOptionDate(item, "created_at");
            record.ExpiresAt = FirstDate(
                Date(item, "expiration_date"),
                Date(item, "expires_at"),
                FirstOptionDate(item, "expiration_date"),
                FirstOptionDate(item, "expires_at"));
            record.TotalAmount = EstimateTotal(item);
            record.SourceUpdatedAt = Date(item, "updated_at");
            record.LastSyncedAt = syncedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Housecall Pro sync stored {JobCount} jobs and {EstimateCount} estimates for {Operation}.", jobs.Count, estimates.Count, operation.Name);
    }

    public async Task<HcpRecordDetail?> GetDetailAsync(string operationSlug, string kind, string externalId, CancellationToken cancellationToken = default)
    {
        var key = $"hcp-detail:{operationSlug}:{kind}:{externalId}";
        return await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            var apiKey = settings.GetApiKey(operationSlug);
            if (apiKey is null) return null;
            var baseEndpoint = kind.Equals("job", StringComparison.OrdinalIgnoreCase) ? settings.JobsEndpoint : settings.EstimatesEndpoint;
            using var document = await GetDocumentAsync($"{baseEndpoint.TrimEnd('/')}/{Uri.EscapeDataString(externalId)}", apiKey, cancellationToken);
            var root = document.RootElement.Clone();
            var fields = new List<KeyValuePair<string, string>>
            {
                new("ID", externalId),
                new("Customer", CustomerName(root) ?? "Not provided"),
                new("Status", Text(root, "work_status") ?? Text(root, "status") ?? FirstOptionText(root, "status") ?? "Not provided"),
                new("Amount", Money(root, "total_amount").ToString("C2")),
                new("Updated", Date(root, "updated_at")?.ToLocalTime().ToString("g") ?? "Not provided")
            };
            var communications = ExtractCommunications(root);
            var json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
            return new HcpRecordDetail(kind, externalId, fields, communications, json);
        });
    }

    public async Task<HcpEstimateStartRecord?> FindEstimateByNumberAsync(
        string operationSlug,
        string estimateNumber,
        CancellationToken cancellationToken = default)
    {
        estimateNumber = estimateNumber.Trim();
        if (string.IsNullOrWhiteSpace(estimateNumber)) return null;
        var parentEstimateNumber = ParentEstimateNumber(estimateNumber);

        var apiKey = settings.GetApiKey(operationSlug);
        if (apiKey is null) return null;

        string? externalId;
        await using (var dataScope = scopeFactory.CreateAsyncScope())
        {
            var db = dataScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            externalId = await db.HousecallProEstimates
                .AsNoTracking()
                .Where(x =>
                    x.LocalOperation.Slug == operationSlug &&
                    (x.EstimateNumber == estimateNumber || x.EstimateNumber == parentEstimateNumber))
                .Select(x => x.ExternalId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        JsonElement estimate;
        if (externalId is not null)
        {
            using var detail = await GetDocumentAsync(
                $"{settings.EstimatesEndpoint.TrimEnd('/')}/{Uri.EscapeDataString(externalId)}",
                apiKey,
                cancellationToken);
            estimate = detail.RootElement.Clone();
        }
        else
        {
            var estimates = await GetPagesAsync(
                settings.EstimatesEndpoint,
                "estimates",
                apiKey,
                cancellationToken,
                maxPages: int.MaxValue);
            estimate = estimates.FirstOrDefault(item =>
            {
                var number = Text(item, "estimate_number") ?? Text(item, "number");
                return string.Equals(number, estimateNumber, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(number, parentEstimateNumber, StringComparison.OrdinalIgnoreCase);
            });
            if (estimate.ValueKind == JsonValueKind.Undefined) return null;
            externalId = Text(estimate, "id");
            if (externalId is null) return null;
        }

        var customerName = CustomerName(estimate);
        var customerAddress = CustomerAddress(estimate);
        if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(customerAddress))
        {
            return null;
        }

        return new HcpEstimateStartRecord(
            externalId,
            Text(estimate, "estimate_number") ?? Text(estimate, "number") ?? estimateNumber,
            customerName,
            CustomerValue(estimate, "email"),
            FirstNonEmpty(
                CustomerValue(estimate, "mobile_number"),
                CustomerValue(estimate, "phone_number"),
                CustomerValue(estimate, "home_number"),
                CustomerValue(estimate, "work_number")),
            customerAddress,
            Date(estimate, "created_at") ?? FirstOptionDate(estimate, "created_at"));
    }

    public async Task<IReadOnlyList<HcpEstimateStartRecord>> SearchEstimateStartsAsync(
        string operationSlug,
        string search,
        CancellationToken cancellationToken = default)
    {
        search = search.Trim();
        if (string.IsNullOrWhiteSpace(search)) return [];

        var parentEstimateNumber = ParentEstimateNumber(search);
        var term = $"%{search}%";
        await using var dataScope = scopeFactory.CreateAsyncScope();
        var db = dataScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.HousecallProEstimates.AsNoTracking()
            .Where(x => x.LocalOperation.Slug == operationSlug)
            .Where(x =>
                x.EstimateNumber == search ||
                x.EstimateNumber == parentEstimateNumber ||
                EF.Functions.ILike(x.CustomerName ?? "", term) ||
                EF.Functions.ILike(x.CustomerAddress ?? "", term) ||
                EF.Functions.ILike(x.CustomerEmail ?? "", term) ||
                EF.Functions.ILike(x.CustomerPhone ?? "", term))
            .OrderByDescending(x => x.EstimateNumber == search || x.EstimateNumber == parentEstimateNumber)
            .ThenByDescending(x => x.EstimateDate)
            .Take(25)
            .Select(x => new HcpEstimateStartRecord(
                x.ExternalId,
                x.EstimateNumber ?? x.ExternalId,
                x.CustomerName ?? "Not provided",
                x.CustomerEmail,
                x.CustomerPhone,
                x.CustomerAddress ?? "Not provided",
                x.EstimateDate))
            .ToListAsync(cancellationToken);
    }

    private static string ParentEstimateNumber(string value)
    {
        var separator = value.LastIndexOf('-');
        return separator > 0 && int.TryParse(value[(separator + 1)..], out _)
            ? value[..separator]
            : value;
    }

    public async Task<HcpJobPaymentHistory?> GetJobPaymentHistoryAsync(
        string operationSlug,
        string externalId,
        CancellationToken cancellationToken = default)
    {
        var key = $"hcp-job-payments:{operationSlug}:{externalId}";
        return await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            var apiKey = settings.GetApiKey(operationSlug);
            if (apiKey is null) return null;

            using var document = await GetDocumentAsync(
                $"{settings.JobsEndpoint.TrimEnd('/')}/{Uri.EscapeDataString(externalId)}/invoices",
                apiKey,
                cancellationToken);
            var transactions = new List<HcpJobPayment>();
            if (!document.RootElement.TryGetProperty("invoices", out var invoices) ||
                invoices.ValueKind != JsonValueKind.Array)
            {
                return new HcpJobPaymentHistory(transactions);
            }

            foreach (var invoice in invoices.EnumerateArray())
            {
                var invoiceNumber = Text(invoice, "invoice_number") ?? "Not provided";
                if (invoice.TryGetProperty("payments", out var payments) && payments.ValueKind == JsonValueKind.Array)
                {
                    foreach (var payment in payments.EnumerateArray())
                    {
                        transactions.Add(new(
                            invoiceNumber,
                            "Payment",
                            Text(payment, "status") ?? "Not provided",
                            Text(payment, "payment_method") ?? "Not provided",
                            Money(payment, "amount"),
                            Text(payment, "note"),
                            Text(payment, "category"),
                            Date(payment, "paid_at") ?? Date(payment, "created_at")));
                    }
                }

                if (invoice.TryGetProperty("refunds", out var refunds) && refunds.ValueKind == JsonValueKind.Array)
                {
                    foreach (var refund in refunds.EnumerateArray())
                    {
                        transactions.Add(new(
                            invoiceNumber,
                            "Refund",
                            Text(refund, "status") ?? "Not provided",
                            Text(refund, "payment_method") ?? "Not provided",
                            -Math.Abs(Money(refund, "amount")),
                            Text(refund, "note"),
                            Text(refund, "category"),
                            Date(refund, "refunded_at") ?? Date(refund, "created_at")));
                    }
                }
            }

            return new HcpJobPaymentHistory(
                transactions.OrderByDescending(x => x.PaidAt).ToList());
        });
    }

    private static IReadOnlyList<HcpImportedCommunication> ExtractCommunications(JsonElement item)
    {
        var result = new List<HcpImportedCommunication>();
        if (item.TryGetProperty("customer", out var customer))
        {
            var customerNotes = Text(customer, "notes");
            if (customerNotes is not null)
                result.Add(new("Customer note", customerNotes, "Housecall Pro (author not supplied)", null));
        }
        if (item.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
        {
            foreach (var option in options.EnumerateArray())
            {
                if (!option.TryGetProperty("notes", out var notes) || notes.ValueKind != JsonValueKind.Array) continue;
                foreach (var note in notes.EnumerateArray())
                {
                    var content = Text(note, "content");
                    if (content is not null)
                        result.Add(new("Estimate note", content, CreatorName(note) ?? "Housecall Pro (author not supplied)", Date(note, "created_at")));
                }
            }
        }
        return result;
    }

    private async Task<List<JsonElement>> GetPagesAsync(
        string endpoint,
        string collection,
        string apiKey,
        CancellationToken cancellationToken,
        int? maxPages = null)
    {
        var result = new List<JsonElement>();
        var page = 1;
        var totalPages = 1;
        do
        {
            using var document = await GetDocumentAsync($"{endpoint}{(endpoint.Contains('?') ? '&' : '?')}page={page}&page_size=100", apiKey, cancellationToken);
            if (document.RootElement.TryGetProperty(collection, out var array) && array.ValueKind == JsonValueKind.Array)
                result.AddRange(array.EnumerateArray().Select(x => x.Clone()));
            if (document.RootElement.TryGetProperty("total_pages", out var pages) && pages.TryGetInt32(out var count)) totalPages = count;
            page++;
        } while (page <= Math.Min(totalPages, Math.Max(1, maxPages ?? settings.RecordSyncMaxPages)));
        return result;
    }

    private async Task<JsonDocument> GetDocumentAsync(string endpoint, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(settings.BaseUrl), endpoint));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string? Text(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value)) return null;
        var text = value.ToString().Trim();
        return string.IsNullOrWhiteSpace(text)
            || text.Equals("undefined", StringComparison.OrdinalIgnoreCase)
            || text.Equals("null", StringComparison.OrdinalIgnoreCase)
            ? null
            : text;
    }
    private static DateTimeOffset? Date(JsonElement item, string name) => DateTimeOffset.TryParse(Text(item, name), out var value) ? value : null;
    private static DateTimeOffset? NestedDate(JsonElement item, string parent, string name) => item.TryGetProperty(parent, out var nested) ? Date(nested, name) : null;
    private static decimal Money(JsonElement item, string name) => decimal.TryParse(Text(item, name), out var cents) ? cents / 100m : 0;
    private static string? CustomerName(JsonElement item)
    {
        if (!item.TryGetProperty("customer", out var customer)) return null;
        return string.Join(' ', new[] { Text(customer, "first_name"), Text(customer, "last_name") }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }
    private static string? CreatorName(JsonElement item)
    {
        foreach (var propertyName in new[] { "created_by", "created_by_employee", "creator" })
        {
            if (!item.TryGetProperty(propertyName, out var creator) || creator.ValueKind != JsonValueKind.Object) continue;
            var name = string.Join(' ', new[] { Text(creator, "first_name"), Text(creator, "last_name") }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (!string.IsNullOrWhiteSpace(name)) return name;
            var displayName = Text(creator, "name");
            if (!string.IsNullOrWhiteSpace(displayName)) return displayName;
        }
        var explicitName = Text(item, "created_by_name");
        if (!string.IsNullOrWhiteSpace(explicitName)) return explicitName;

        if (item.TryGetProperty("assigned_employees", out var employees) && employees.ValueKind == JsonValueKind.Array)
        {
            foreach (var employee in employees.EnumerateArray())
            {
                var assignedName = string.Join(' ', new[] { Text(employee, "first_name"), Text(employee, "last_name") }.Where(x => !string.IsNullOrWhiteSpace(x)));
                if (!string.IsNullOrWhiteSpace(assignedName)) return assignedName;
                var assignedDisplayName = Text(employee, "name");
                if (!string.IsNullOrWhiteSpace(assignedDisplayName)) return assignedDisplayName;
            }
        }

        return null;
    }
    private static string? CustomerValue(JsonElement item, string name) =>
        item.TryGetProperty("customer", out var customer) ? Text(customer, name) : null;
    private static string? CustomerAddress(JsonElement item)
    {
        if (!item.TryGetProperty("customer", out var customer)) return Address(item);
        if (customer.TryGetProperty("addresses", out var addresses) && addresses.ValueKind == JsonValueKind.Array)
        {
            foreach (var address in addresses.EnumerateArray())
            {
                var formatted = FormatAddress(address);
                if (!string.IsNullOrWhiteSpace(formatted)) return formatted;
            }
        }
        if (customer.TryGetProperty("address", out var nestedAddress))
        {
            var formatted = FormatAddress(nestedAddress);
            if (!string.IsNullOrWhiteSpace(formatted)) return formatted;
        }
        return Address(item);
    }
    private static string? Address(JsonElement item)
    {
        if (!item.TryGetProperty("address", out var address)) return null;
        return FormatAddress(address);
    }
    private static string? FormatAddress(JsonElement address)
    {
        var street = FirstNonEmpty(Text(address, "street"), Text(address, "street_line_1"), Text(address, "address_line_1"));
        var street2 = FirstNonEmpty(Text(address, "street_line_2"), Text(address, "address_line_2"));
        var cityStateZip = string.Join(" ", new[] { Text(address, "city"), Text(address, "state"), Text(address, "zip") ?? Text(address, "postal_code") }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var value = string.Join(", ", new[] { street, street2, cityStateZip }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
    private static string? FirstOptionText(JsonElement item, string name) =>
        item.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array
            ? options.EnumerateArray().Select(x => Text(x, name)).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            : null;
    private static DateTimeOffset? FirstOptionDate(JsonElement item, string name) =>
        item.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array
            ? options.EnumerateArray().Select(x => Date(x, name)).FirstOrDefault(x => x.HasValue)
            : null;
    private static T? FirstNonEmpty<T>(params T?[] values) where T : class => values.FirstOrDefault(x => x is not null);
    private static DateTimeOffset? FirstDate(params DateTimeOffset?[] values) => values.FirstOrDefault(x => x.HasValue);
    private static decimal EstimateTotal(JsonElement item)
    {
        if (!item.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array) return Money(item, "total_amount");
        return options.EnumerateArray().Sum(x => Money(x, "total_amount"));
    }
}
