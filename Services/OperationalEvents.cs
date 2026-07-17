using System.Diagnostics;
using System.Text.RegularExpressions;
using CharleyCompany.Dashboard.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed record OperationalEventRequest(
    string Service, string Module, string Step, string Status, string Message,
    string Severity = "Information", string? CorrelationId = null, int? LocalOperationId = null,
    int? QuoteCaseId = null, string? JobNumber = null, long? DurationMilliseconds = null,
    string? ErrorCode = null, string? ExceptionSummary = null, string? MetadataJson = null,
    Guid? EventId = null, DateTimeOffset? Timestamp = null, string? Machine = null, string? Environment = null);

public sealed class OperationalEventBroker
{
    public event Func<Task>? EventReceived;
    public async Task NotifyAsync()
    {
        var handlers = EventReceived;
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Func<Task>>()) await handler();
    }
}

public sealed partial class OperationalEventPublisher(
    ApplicationDbContext dbContext,
    OperationalEventBroker broker,
    IWebHostEnvironment environment,
    ILogger<OperationalEventPublisher> logger)
{
    public static readonly ActivitySource ActivitySource = new("CharlieCompany.Operations");

    public async Task<OperationalEvent> PublishAsync(OperationalEventRequest request, CancellationToken cancellationToken = default)
    {
        var correlationId = Clean(request.CorrelationId) ?? Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        var item = new OperationalEvent
        {
            EventId = request.EventId ?? Guid.NewGuid(), Timestamp = request.Timestamp ?? DateTimeOffset.UtcNow,
            Severity = NormalizeSeverity(request.Severity), Environment = Clean(request.Environment) ?? environment.EnvironmentName,
            Machine = Clean(request.Machine) ?? System.Environment.MachineName, Service = Clean(request.Service) ?? "Unknown",
            Module = Clean(request.Module) ?? "Unknown", Step = Clean(request.Step) ?? "Unknown", Status = Clean(request.Status) ?? "Unknown",
            Message = Redact(request.Message), CorrelationId = correlationId, LocalOperationId = request.LocalOperationId,
            QuoteCaseId = request.QuoteCaseId, JobNumber = Clean(request.JobNumber), DurationMilliseconds = request.DurationMilliseconds,
            ErrorCode = Clean(request.ErrorCode), ExceptionSummary = RedactNullable(request.ExceptionSummary), MetadataJson = RedactNullable(request.MetadataJson)
        };
        if (await dbContext.OperationalEvents.AnyAsync(x => x.EventId == item.EventId, cancellationToken))
            return await dbContext.OperationalEvents.AsNoTracking().SingleAsync(x => x.EventId == item.EventId, cancellationToken);
        dbContext.OperationalEvents.Add(item);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Operational event {EventId} {CorrelationId} {Service}.{Module} {Step} {Status}: {Message}", item.EventId, item.CorrelationId, item.Service, item.Module, item.Step, item.Status, item.Message);
        await broker.NotifyAsync();
        return item;
    }

    private static string NormalizeSeverity(string value) => value.ToLowerInvariant() switch { "trace" => "Trace", "debug" => "Debug", "warning" or "warn" => "Warning", "error" => "Error", "critical" or "fatal" => "Critical", _ => "Information" };
    private static string Redact(string? value) => SensitiveValueRegex().Replace(value ?? string.Empty, "$1=[REDACTED]");
    private static string? RedactNullable(string? value) => value is null ? null : Redact(value);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    [GeneratedRegex("(?i)(password|pwd|api[_-]?key|authorization|bearer|connectionstring|secret|token)\\s*[=:]\\s*[^,;\\s\\\"]+")]
    private static partial Regex SensitiveValueRegex();
}

public sealed class OperationalEventRetentionService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<OperationalEventRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var days = Math.Clamp(configuration.GetValue("Observability:RetentionDays", 30), 1, 3650);
                var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
                var deleted = await db.OperationalEvents.Where(x => x.Timestamp < cutoff).ExecuteDeleteAsync(stoppingToken);
                if (deleted > 0) logger.LogInformation("Deleted {Count} expired operational events", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "Operational event retention failed"); }
        }
    }
}
