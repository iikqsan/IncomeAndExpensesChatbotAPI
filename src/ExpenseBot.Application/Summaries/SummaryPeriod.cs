namespace ExpenseBot.Application.Summaries;

public enum PeriodKind
{
    Day,
    Week,
    Month,
}

/// <summary>
/// A reporting period cut at midnight in the user's timezone, expressed as a UTC range
/// <c>[FromUtc, ToUtc)</c> so the query can use the <c>{userId, occurredAt}</c> index.
/// </summary>
public sealed record SummaryPeriod(PeriodKind Kind, DateOnly FirstDay, DateOnly LastDay, DateTime FromUtc, DateTime ToUtc)
{
    public static SummaryPeriod Today(DateTime nowUtc, TimeZoneInfo tz)
    {
        var today = LocalDate(nowUtc, tz);
        return Create(PeriodKind.Day, today, today.AddDays(1), tz);
    }

    /// <summary>Week starts on Monday.</summary>
    public static SummaryPeriod ThisWeek(DateTime nowUtc, TimeZoneInfo tz)
    {
        var today = LocalDate(nowUtc, tz);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return Create(PeriodKind.Week, monday, monday.AddDays(7), tz);
    }

    public static SummaryPeriod ThisMonth(DateTime nowUtc, TimeZoneInfo tz)
    {
        var today = LocalDate(nowUtc, tz);
        return Month(today.Year, today.Month, tz);
    }

    public static SummaryPeriod Month(int year, int month, TimeZoneInfo tz)
    {
        var first = new DateOnly(year, month, 1);
        return Create(PeriodKind.Month, first, first.AddMonths(1), tz);
    }

    public bool Contains(DateTime utc) => utc >= FromUtc && utc < ToUtc;

    private static DateOnly LocalDate(DateTime nowUtc, TimeZoneInfo tz) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), tz));

    private static SummaryPeriod Create(PeriodKind kind, DateOnly first, DateOnly endExclusive, TimeZoneInfo tz) =>
        new(kind, first, endExclusive.AddDays(-1), MidnightToUtc(first, tz), MidnightToUtc(endExclusive, tz));

    private static DateTime MidnightToUtc(DateOnly localMidnight, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeToUtc(localMidnight.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), tz);
}
