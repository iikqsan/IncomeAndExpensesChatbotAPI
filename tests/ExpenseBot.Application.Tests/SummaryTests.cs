using ExpenseBot.Application.Domain;
using ExpenseBot.Application.Summaries;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExpenseBot.Application.Tests;

public class SummaryTests
{
    private static readonly TimeZoneInfo Bangkok = TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");

    // 01:00 on 21 Nov 2026 in Bangkok == 18:00 on 20 Nov 2026 UTC.
    private static readonly DateTime OneAmBangkok21Nov = new(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Today_is_cut_at_midnight_bangkok()
    {
        var p = SummaryPeriod.Today(new DateTime(2026, 11, 21, 5, 0, 0, DateTimeKind.Utc), Bangkok);

        Assert.Equal(new DateOnly(2026, 11, 21), p.FirstDay);
        Assert.Equal(new DateTime(2026, 11, 20, 17, 0, 0, DateTimeKind.Utc), p.FromUtc);
        Assert.Equal(new DateTime(2026, 11, 21, 17, 0, 0, DateTimeKind.Utc), p.ToUtc);
    }

    [Fact]
    public void Transaction_at_1am_thai_time_belongs_to_that_day_not_the_previous_one()
    {
        var on21st = SummaryPeriod.Today(new DateTime(2026, 11, 21, 5, 0, 0, DateTimeKind.Utc), Bangkok);
        var on20th = SummaryPeriod.Today(new DateTime(2026, 11, 20, 5, 0, 0, DateTimeKind.Utc), Bangkok);

        Assert.True(on21st.Contains(OneAmBangkok21Nov));
        Assert.False(on20th.Contains(OneAmBangkok21Nov));
    }

    [Fact]
    public void Today_uses_local_date_even_when_utc_date_is_still_yesterday()
    {
        // 00:30 Bangkok on the 21st is 17:30 UTC on the 20th.
        var p = SummaryPeriod.Today(new DateTime(2026, 11, 20, 17, 30, 0, DateTimeKind.Utc), Bangkok);
        Assert.Equal(new DateOnly(2026, 11, 21), p.FirstDay);
    }

    [Fact]
    public void Week_starts_monday()
    {
        var p = SummaryPeriod.ThisWeek(new DateTime(2026, 11, 21, 5, 0, 0, DateTimeKind.Utc), Bangkok); // Saturday
        Assert.Equal(new DateOnly(2026, 11, 16), p.FirstDay);
        Assert.Equal(new DateOnly(2026, 11, 22), p.LastDay);
        Assert.Equal(new DateTime(2026, 11, 15, 17, 0, 0, DateTimeKind.Utc), p.FromUtc);
    }

    [Fact]
    public void Month_range_and_first_hour_of_month()
    {
        var nov = SummaryPeriod.Month(2026, 11, Bangkok);
        var oct = SummaryPeriod.Month(2026, 10, Bangkok);
        var oneAmNov1 = new DateTime(2026, 10, 31, 18, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc), nov.FromUtc);
        Assert.Equal(new DateTime(2026, 11, 30, 17, 0, 0, DateTimeKind.Utc), nov.ToUtc);
        Assert.True(nov.Contains(oneAmNov1));
        Assert.False(oct.Contains(oneAmNov1));
    }

    [Fact]
    public async Task Service_counts_1am_transaction_in_today_and_ignores_deleted_and_other_users()
    {
        var repo = new FakeTransactions();
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 21, 5, 0, 0, TimeSpan.Zero));
        var service = new SummaryService(repo, clock, NullLogger<SummaryService>.Instance);
        var user = new BotUser { Id = 1, Timezone = "Asia/Bangkok" };

        Add(repo, 1, 1, TransactionType.Expense, 60, "food", OneAmBangkok21Nov);
        Add(repo, 1, 2, TransactionType.Expense, 40, "food", OneAmBangkok21Nov.AddHours(-2));   // 23:00 on 20th
        Add(repo, 1, 3, TransactionType.Income, 1000, "salary", OneAmBangkok21Nov.AddHours(3));
        Add(repo, 1, 4, TransactionType.Expense, 500, "food", OneAmBangkok21Nov, deleted: true);
        Add(repo, 2, 1, TransactionType.Expense, 999, "food", OneAmBangkok21Nov);               // someone else

        var report = await service.TodayAsync(user, default);

        Assert.Equal(60m, report.Expense);
        Assert.Equal(1000m, report.Income);
        Assert.Equal(900m, report.Balance);   // no payday yet: calendar-month balance (1000 - 60 - 40)
    }

    // ---------- Pay cycles ----------

    private static DateOnly D(int month, int day, int year = 2026) => new(year, month, day);

    [Fact]
    public void Salary_entries_close_to_the_cycle_start_stay_in_the_same_cycle()
    {
        var starts = PayCycles.CycleStarts([D(11, 25), D(10, 25), D(10, 27), D(10, 25)]);
        Assert.Equal([D(10, 25), D(11, 25)], starts);
    }

    [Fact]
    public void Current_cycle_runs_from_salary_to_the_day_before_next_expected_salary()
    {
        var cycle = PayCycles.Current([D(10, 31)], today: D(11, 22));
        Assert.Equal(new PayCycle(D(10, 31), D(11, 29), IsOngoing: true), cycle);
    }

    [Theory]
    [InlineData(2026, 11, 25, 2026, 12, 24)]  // fixed payday: day before the 25th
    [InlineData(2026, 10, 31, 2026, 11, 29)]  // month end → next is 30 Nov
    [InlineData(2026, 11, 30, 2026, 12, 30)]  // month end → next is 31 Dec
    [InlineData(2026, 12, 31, 2027, 1, 30)]   // month end → next is 31 Jan
    [InlineData(2027, 1, 31, 2027, 2, 27)]    // month end → next is 28 Feb
    [InlineData(2027, 2, 28, 2027, 3, 30)]    // month end → next is 31 Mar
    [InlineData(2026, 10, 30, 2026, 11, 29)]  // 30 Oct is not month end → next is 30 Nov
    [InlineData(2027, 1, 30, 2027, 2, 27)]    // 30 Jan → clamped to 28 Feb
    public void Expected_cycle_end_follows_the_real_calendar(int y, int m, int d, int ey, int em, int ed)
    {
        Assert.Equal(new DateOnly(ey, em, ed), PayCycles.ExpectedLastDay(new DateOnly(y, m, d)));
    }

    [Fact]
    public void Current_cycle_is_null_without_a_recent_salary()
    {
        Assert.Null(PayCycles.Current([], D(11, 22)));
        Assert.Null(PayCycles.Current([D(8, 1)], D(11, 22)));
    }

    [Fact]
    public void Closed_cycle_ends_the_day_before_the_next_salary()
    {
        var cycle = PayCycles.ForMonth([D(10, 31), D(11, 30)], 2026, 10, today: D(12, 5));
        Assert.Equal(new PayCycle(D(10, 31), D(11, 29), IsOngoing: false), cycle);
    }

    [Fact]
    public void Old_cycle_without_a_next_salary_is_capped_at_one_month()
    {
        var cycle = PayCycles.ForMonth([D(6, 25)], 2026, 6, today: D(11, 22));
        Assert.Equal(new PayCycle(D(6, 25), D(7, 24), IsOngoing: false), cycle);
    }

    [Fact]
    public void Pay_cycle_period_is_cut_at_midnight_bangkok()
    {
        var p = SummaryPeriod.ForPayCycle(new PayCycle(D(10, 25), D(11, 24), false), Bangkok);
        Assert.Equal(new DateTime(2026, 10, 24, 17, 0, 0, DateTimeKind.Utc), p.FromUtc);
        Assert.Equal(new DateTime(2026, 11, 24, 17, 0, 0, DateTimeKind.Utc), p.ToUtc);
        Assert.Equal(D(11, 24), p.LastDay);
    }

    private static void Add(FakeTransactions repo, long userId, long seq, TransactionType type, decimal amount,
        string categoryId, DateTime occurredAt, bool deleted = false)
    {
        repo.Items.Add(new Transaction
        {
            Id = Guid.NewGuid().ToString("N"), UserId = userId, Seq = seq, MessageId = seq, Type = type,
            Amount = amount, CategoryId = categoryId, OccurredAt = occurredAt, IsDeleted = deleted,
        });
    }
}
