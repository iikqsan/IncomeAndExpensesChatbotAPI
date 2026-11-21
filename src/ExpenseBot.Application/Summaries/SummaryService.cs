using ExpenseBot.Application.Abstractions;
using ExpenseBot.Application.Domain;
using Microsoft.Extensions.Logging;

namespace ExpenseBot.Application.Summaries;

public sealed record SummaryReport(SummaryPeriod Period, IReadOnlyList<CategoryTotal> Lines)
{
    public decimal Income => Lines.Where(l => l.Type == TransactionType.Income).Sum(l => l.Total);
    public decimal Expense => Lines.Where(l => l.Type == TransactionType.Expense).Sum(l => l.Total);
    public decimal Balance => Income - Expense;
}

public sealed class SummaryService(ITransactionRepository transactions, TimeProvider clock, ILogger<SummaryService> logger)
{
    public Task<SummaryReport> TodayAsync(BotUser user, CancellationToken ct) =>
        SummarizeAsync(user, SummaryPeriod.Today(Now, TimeZoneFor(user)), ct);

    public Task<SummaryReport> ThisWeekAsync(BotUser user, CancellationToken ct) =>
        SummarizeAsync(user, SummaryPeriod.ThisWeek(Now, TimeZoneFor(user)), ct);

    public Task<SummaryReport> ThisMonthAsync(BotUser user, CancellationToken ct) =>
        SummarizeAsync(user, SummaryPeriod.ThisMonth(Now, TimeZoneFor(user)), ct);

    public Task<SummaryReport> MonthAsync(BotUser user, int year, int month, CancellationToken ct) =>
        SummarizeAsync(user, SummaryPeriod.Month(year, month, TimeZoneFor(user)), ct);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

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
