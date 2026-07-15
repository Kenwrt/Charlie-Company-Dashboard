namespace CharleyCompany.Dashboard.Web.Services;

public sealed record NotificationMessage(
    string Subject,
    string Body,
    string EventType,
    DateTimeOffset OccurredAt);

