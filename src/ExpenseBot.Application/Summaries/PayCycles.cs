using ExpenseBot.Application.Domain;

namespace ExpenseBot.Application.Summaries;

/// <summary>
/// Pay-cycle rules for /month. Pure functions: no I/O, no clock.
/// A cycle starts on the local date a salary was recorded and ends the day before the next salary.
/// </summary>
public static class PayCycles
{
    public const string SalaryCategoryId = "salary";

    /// <summary>The "salary" category also catches bonus/OT; only entries that say this word start a cycle.</summary>
    public const string SalaryWord = "เงินเดือน";

    /// <summary>Salary entries closer than this to the cycle start (e.g. a split payment) stay in the same cycle.</summary>
    public const int MinDaysBetweenPaydays = 20;

    /// <summary>A cycle with no next salary yet stays open (up to today) for at most this many days.</summary>
    public const int MaxOpenCycleDays = 62;

    public static bool IsPayday(Transaction t) =>
        t.Type == TransactionType.Income
        && t.CategoryId == SalaryCategoryId
        && (t.Description.Contains(SalaryWord, StringComparison.Ordinal) || t.RawText.Contains(SalaryWord, StringComparison.Ordinal));

    /// <summary>Distinct, ascending cycle start dates from the local dates of payday entries.</summary>
    public static IReadOnlyList<DateOnly> CycleStarts(IEnumerable<DateOnly> paydays)
    {
        var starts = new List<DateOnly>();
        foreach (var day in paydays.Distinct().Order())
        {
            if (starts.Count == 0 || day.DayNumber - starts[^1].DayNumber >= MinDaysBetweenPaydays)
                starts.Add(day);
        }
        return starts;
    }

    /// <summary>The cycle containing <paramref name="today"/>, or null when there is no recent salary.</summary>
    public static PayCycle? Current(IReadOnlyList<DateOnly> starts, DateOnly today)
    {
        var i = LastIndex(starts, s => s <= today);
        if (i < 0)
            return null;

        var cycle = Resolve(starts, i, today);
        return cycle.IsOngoing ? cycle : null;
    }

    /// <summary>The first cycle whose salary was recorded in the given month, or null.</summary>
    public static PayCycle? ForMonth(IReadOnlyList<DateOnly> starts, int year, int month, DateOnly today)
    {
        for (var i = 0; i < starts.Count; i++)
        {
            if (starts[i].Year == year && starts[i].Month == month && starts[i] <= today)
                return Resolve(starts, i, today);
        }
        return null;
    }

    private static PayCycle Resolve(IReadOnlyList<DateOnly> starts, int i, DateOnly today)
    {
        var first = starts[i];
        if (i + 1 < starts.Count && starts[i + 1] <= today)
            return new PayCycle(first, starts[i + 1].AddDays(-1), IsOngoing: false);

        var expectedLast = ExpectedLastDay(first);
        if (today.DayNumber - first.DayNumber < MaxOpenCycleDays)
        {
            // Show the whole expected cycle (e.g. 25 Nov – 24 Dec); stretch it if the next salary is late.
            return new PayCycle(first, today > expectedLast ? today : expectedLast, IsOngoing: true);
        }

        // Old cycle that was never followed by another salary: cap it at one month.
        return new PayCycle(first, expectedLast, IsOngoing: false);
    }

    /// <summary>The day before the next salary is expected.</summary>
    public static DateOnly ExpectedLastDay(DateOnly first) => ExpectedNextPayday(first).AddDays(-1);

    /// <summary>
    /// Same day next month (25 Nov → 25 Dec, clamped: 30 Jan → 28 Feb). A salary on the last day of the
    /// month is paid at month end, so the next one is the last day of next month (30 Nov → 31 Dec).
    /// </summary>
    public static DateOnly ExpectedNextPayday(DateOnly first)
    {
        var next = first.AddMonths(1);
        var isMonthEnd = first.Day == DateTime.DaysInMonth(first.Year, first.Month);
        return isMonthEnd ? new DateOnly(next.Year, next.Month, DateTime.DaysInMonth(next.Year, next.Month)) : next;
    }

    private static int LastIndex(IReadOnlyList<DateOnly> list, Func<DateOnly, bool> match)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (match(list[i]))
                return i;
        }
        return -1;
    }
}

/// <summary>
/// Local dates, both inclusive. <see cref="IsOngoing"/>: no next salary yet, so it runs up to the
/// expected next payday (or today, if that is later).
/// </summary>
public sealed record PayCycle(DateOnly FirstDay, DateOnly LastDay, bool IsOngoing);
