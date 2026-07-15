using System.Text.Json;
using CharleyCompany.Dashboard.Web.Models;
using CharleyCompany.Dashboard.Web.Services;

namespace CharleyCompany.Dashboard.Web.Endpoints;

public static class HousecallProWebhookEndpoints
{
    public static IEndpointRouteBuilder MapHousecallProWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/housecallpro/webhooks", async (
            HttpRequest request,
            DashboardNotificationService notifications,
            WebhookNotificationDispatcher notificationDispatcher,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("HousecallProWebhooks");

            try
            {
                using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
                var root = document.RootElement;
                var eventType = ReadString(root, "event", "event_type", "type") ?? "housecallpro.event";
                var message = BuildMessage(eventType, root);

                logger.LogInformation("Received Housecall Pro webhook event {EventType}: {Payload}", eventType, root.ToString());

                var dashboardEvent = new DashboardEvent(
                    eventType,
                    message,
                    DateTimeOffset.Now,
                    "Housecall Pro webhook");

                await notifications.PublishAsync(dashboardEvent);
                await notificationDispatcher.DispatchAsync(dashboardEvent, cancellationToken);

                return Results.Accepted();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process Housecall Pro webhook payload.");
                Console.Error.WriteLine(ex);
                return Results.BadRequest("Webhook payload could not be processed. See application logs.");
            }
        })
        .AllowAnonymous()
        .WithName("HousecallProWebhooks");

        return endpoints;
    }

    private static string BuildMessage(string eventType, JsonElement root)
    {
        if (eventType.Contains("quote", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("estimate", StringComparison.OrdinalIgnoreCase))
        {
            return "A quote event was received and the dashboard is refreshing.";
        }

        if (eventType.Contains("expense", StringComparison.OrdinalIgnoreCase))
        {
            return "An expense event was received and the dashboard is refreshing.";
        }

        return "A Housecall Pro event was received and the dashboard is refreshing.";
    }

    private static string? ReadString(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        return null;
    }
}
