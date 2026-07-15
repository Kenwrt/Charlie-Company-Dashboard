namespace CharleyCompany.Dashboard.Web.Models;

public sealed record DashboardEvent(
    string EventType,
    string Message,
    DateTimeOffset OccurredAt,
    string Source);

