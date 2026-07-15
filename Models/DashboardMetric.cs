namespace CharleyCompany.Dashboard.Web.Models;

public sealed record DashboardMetric(
    string Label,
    string Value,
    string Detail,
    string AccentClass);

