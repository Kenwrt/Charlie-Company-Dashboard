namespace CharleyCompany.Dashboard.Web.Services;

internal static class EstimateAge
{
    public const string Legend = "Green = 0–15 days, yellow = 16–30 days, red = 31 days or older.";

    public static string? Background(DateTimeOffset? estimateDate, DateOnly asOf)
    {
        if (estimateDate is not { } date)
            return null;

        var days = asOf.DayNumber - DateOnly.FromDateTime(date.Date).DayNumber;
        return days switch
        {
            < 0 => null,
            <= 15 => "#d1e7dd",
            <= 30 => "#fff3cd",
            _ => "#f8d7da"
        };
    }
}
