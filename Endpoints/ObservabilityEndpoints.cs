using System.Security.Cryptography;
using System.Text;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace CharleyCompany.Dashboard.Web.Endpoints;

public static class ObservabilityEndpoints
{
    public static IEndpointRouteBuilder MapObservabilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/observability/events", async (HttpContext context, OperationalEventRequest request, IConfiguration configuration, ApplicationDbContext db, OperationalEventPublisher publisher, CancellationToken ct) =>
        {
            var configured = configuration["Observability:IngestionKey"];
            if (string.IsNullOrWhiteSpace(configured)) return Results.Problem("Observability ingestion is not configured.", statusCode: 503);
            var supplied = context.Request.Headers["X-CCV-Observability-Key"].ToString();
            if (!FixedEquals(configured, supplied)) return Results.Unauthorized();
            if (request.LocalOperationId is int operationId && !await db.LocalOperations.AnyAsync(x => x.Id == operationId, ct)) return Results.BadRequest(new { error = "Unknown local operation." });
            var item = await publisher.PublishAsync(request, ct);
            return Results.Accepted($"/api/observability/events/{item.EventId}", new { item.EventId, item.CorrelationId, item.ReceivedAt });
        }).AllowAnonymous().RequireRateLimiting("observability-ingestion");

        endpoints.MapGet("/api/observability/export", async (ApplicationDbContext db, string? correlationId, int? operationId, CancellationToken ct) =>
        {
            var query = db.OperationalEvents.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(correlationId)) query = query.Where(x => x.CorrelationId == correlationId);
            if (operationId is not null) query = query.Where(x => x.LocalOperationId == operationId);
            var events = await query.OrderBy(x => x.Timestamp).Take(5000).ToListAsync(ct);
            var csv = new StringBuilder("Timestamp,Severity,Environment,Machine,Service,Module,OperationId,CorrelationId,QuoteId,JobNumber,Step,Status,DurationMs,Message,ErrorCode\r\n");
            foreach (var item in events) csv.AppendLine(string.Join(',', new[] { item.Timestamp.ToString("O"), item.Severity, item.Environment, item.Machine, item.Service, item.Module, item.LocalOperationId?.ToString(), item.CorrelationId, item.QuoteCaseId?.ToString(), item.JobNumber, item.Step, item.Status, item.DurationMilliseconds?.ToString(), item.Message, item.ErrorCode }.Select(Csv)));
            return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"ccv-operational-events-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
        }).RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Administrator));
        return endpoints;
    }

    private static bool FixedEquals(string expected, string actual)
    {
        var left = Encoding.UTF8.GetBytes(expected); var right = Encoding.UTF8.GetBytes(actual);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}
