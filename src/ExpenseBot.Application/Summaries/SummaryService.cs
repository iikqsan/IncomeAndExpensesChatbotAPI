using ExpenseBot.Application.Abstractions;
using ExpenseBot.Application.Domain;
using Microsoft.Extensions.Logging;

namespace ExpenseBot.Application.Summaries;

/// <param name="RemainingBalance">
/// For /today and /week: what is left of the current pay cycle (or calendar month) after this period's spending.
/// Null for /month, where the balance is the period's income minus expense.
/// </param>
public sealed record SummaryReport(SummaryPeriod Period, IReadOnlyList<CategoryTotal> Lines, decimal? RemainingBalance = null)
{
    public decimal Income => Lines.Where(l => l.Type == TransactionType.Income).Sum(l => l.Total);
    public decimal Expense => Lines.Where(l => l.Type == TransactionType.Expense).Sum(l => l.Total);
    public decimal Balance => RemainingBalance ?? Income - Expense;
}

public sealed class SummaryService(ITransactionRepository transactions, TimeProvider clock, ILogger<SummaryService> logger)
{
    /// <summary>Today's lines; the balance is what is left of the current pay cycle.</summary>
    public Task<SummaryReport> TodayAsync(BotUser user, CancellationToken ct) =>
        SummarizeWithRemainingAsync(user, SummaryPeriod.Today(Now, TimeZoneFor(user)), ct);

    /// <summary>This week's lines; the balance is what is left of the current pay cycle.</summary>
    public Task<SummaryReport> ThisWeekAsync(BotUser user, CancellationToken ct) =>
        SummarizeWithRemainingAsync(user, SummaryPeriod.ThisWeek(Now, TimeZoneFor(user)), ct);

    /// <summary>The current pay cycle; the calendar month when no salary was recorded recently.</summary>
    public async Task<SummaryReport> ThisMonthAsync(BotUser user, CancellationToken ct) =>
        await SummarizeAsync(user, await CurrentCyclePeriodAsync(user, TimeZoneFor(user), ct), ct);

    private async Task<SummaryPeriod> CurrentCyclePeriodAsync(BotUser user, TimeZoneInfo tz, CancellationToken ct)
    {
        var today = SummaryPeriod.LocalDate(Now, tz);
        var starts = await CycleStartsAsync(user,
            today.AddDays(-(PayCycles.MaxOpenCycleDays + PayCycles.MinDaysBetweenPaydays)), today.AddDays(1), tz, ct);

        var cycle = PayCycles.Current(starts, today);
        return cycle is null ? SummaryPeriod.ThisMonth(Now, tz) : SummaryPeriod.ForPayCycle(cycle, tz);
    }

    private async Task<SummaryReport> SummarizeWithRemainingAsync(BotUser user, SummaryPeriod period, CancellationToken ct)
    {
        var report = await SummarizeAsync(user, period, ct);
        var cycle = await SummarizeAsync(user, await CurrentCyclePeriodAsync(user, TimeZoneFor(user), ct), ct);
        return report with { RemainingBalance = cycle.Income - cycle.Expense };
    }

    /// <summary>The pay cycle whose salary was recorded in that month; the calendar month when there is none.</summary>
    public async Task<SummaryReport> MonthAsync(BotUser user, int year, int month, CancellationToken ct)
    {
        var tz = TimeZoneFor(user);
        var today = SummaryPeriod.LocalDate(Now, tz);
        var first = new DateOnly(year, month, 1);
        var starts = await CycleStartsAsync(user,
            first.AddDays(-PayCycles.MinDaysBetweenPaydays), first.AddMonths(1).AddDays(PayCycles.MaxOpenCycleDays), tz, ct);

        var cycle = PayCycles.ForMonth(starts, year, month, today);
        var period = cycle is null ? SummaryPeriod.Month(year, month, tz) : SummaryPeriod.ForPayCycle(cycle, tz);
        return await SummarizeAsync(user, period, ct);
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Cycle starts from salary entries with local dates in <c>[from, toExclusive)</c>.</summary>
    private async Task<IReadOnlyList<DateOnly>> CycleStartsAsync(
        BotUser user, DateOnly from, DateOnly toExclusive, TimeZoneInfo tz, CancellationToken ct)
    {
        var salaries = await transactions.GetByCategoryAsync(user.Id, PayCycles.SalaryCategoryId,
            SummaryPeriod.MidnightToUtc(from, tz), SummaryPeriod.MidnightToUtc(toExclusive, tz), ct);

        return PayCycles.CycleStarts(salaries
            .Where(PayCycles.IsPayday)
            .Select(t => SummaryPeriod.LocalDate(t.OccurredAt, tz)));
    }

    private async Task<SummaryReport> SummarizeAsync(BotUser user, SummaryPeriod period, CancellationToken ct)
    {
        var lines = await transactions.SummarizeAsync(user.Id, period.FromUtc, period.ToUtc, ct);
        return new SummaryReport(period, lines);
    }

    private TimeZoneInfo TimeZoneFor(BotUser user)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(user.Timezone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning("Unknown timezone {Timezone} for user {UserId}, using Asia/Bangkok", user.Timezone, user.Id);
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");
        }
    }
}
