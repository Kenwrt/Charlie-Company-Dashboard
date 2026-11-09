namespace CharleyCompany.Dashboard.Web.Services;

internal static class EstimateAge
{
    public const string Legend = "Age from estimate date: green = 0–15 days, yellow = 16–30 days, red = 31 days or older.";

    public static string? TextColor(DateTimeOffset? estimateDate, DateOnly asOf)
    {
        if (estimateDate is not { } date)
            return null;

        var days = asOf.DayNumber - DateOnly.FromDateTime(date.Date).DayNumber;
        return days switch
        {
            < 0 => null,
            <= 15 => "#146c43",
            <= 30 => "#8a6500",
            _ => "#b02a37"
        };
    }
}
