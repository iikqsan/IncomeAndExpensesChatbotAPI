using ExpenseBot.Application.Abstractions;
using ExpenseBot.Application.Categories;
using ExpenseBot.Application.Domain;
using ExpenseBot.Application.Formatting;
using ExpenseBot.Application.Summaries;

namespace ExpenseBot.Application.Tests;

public class TransactionFormatterTests
{
    private sealed class FakeCategories : ICategoryProvider
    {
        private static readonly Category[] All =
        [
            new() { Id = "food", Name = "อาหาร", Type = CategoryType.Expense, SortOrder = 10 },
            new() { Id = "weird", Name = "A&B <x>", Type = CategoryType.Expense, SortOrder = 20 },
            new() { Id = DefaultCategories.ExpenseOther, Name = "อื่นๆ", Type = CategoryType.Expense, SortOrder = 99 },
            new() { Id = "salary", Name = "เงินเดือน", Type = CategoryType.Income, SortOrder = 110 },
        ];

        public IReadOnlyList<Category> GetAll() => All;

        public Category? GetById(string id) => All.FirstOrDefault(c => c.Id == id);
    }

    private readonly TransactionFormatter _formatter = new(new FakeCategories());

    private static Transaction Tx(TransactionType type, decimal amount, string description, string categoryId, long seq = 42) =>
        new() { Seq = seq, Type = type, Amount = amount, Description = description, CategoryId = categoryId };

    [Fact]
    public void Expense()
    {
        var text = _formatter.Format(Tx(TransactionType.Expense, 60m, "ซื้อข้าวกลางวัน", "food"));
        Assert.Equal("🔴\n#42 รายการจ่าย\nซื้อข้าวกลางวัน : 60 บาท", text);
    }

    [Fact]
    public void Income_with_thousands()
    {
        var text = _formatter.Format(Tx(TransactionType.Income, 25000m, "เงินเดือนพฤศจิกายน", "salary", seq: 43));
        Assert.Equal("🟢\n#43 รายการรับ\nเงินเดือนพฤศจิกายน : 25,000 บาท", text);
    }

    [Theory]
    [InlineData("1200", "1,200")]
    [InlineData("1234567", "1,234,567")]
    [InlineData("45.50", "45.50")]
    [InlineData("45.5", "45.50")]
    [InlineData("1200.05", "1,200.05")]
    [InlineData("60.00", "60")]
    [InlineData("999", "999")]
    public void Amount_format(string amount, string expected)
    {
        Assert.Equal(expected, TransactionFormatter.FormatAmount(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Decimal_amount_in_line()
    {
        var text = _formatter.Format(Tx(TransactionType.Expense, 45.50m, "กาแฟ", "food"));
        Assert.Equal("🔴\n#42 รายการจ่าย\nกาแฟ : 45.50 บาท", text);
    }

    [Fact]
    public void Escapes_html_in_description()
    {
        var text = _formatter.Format(Tx(TransactionType.Expense, 10m, "<b>ข้าว</b> & น้ำ", "weird"));
        Assert.Equal("🔴\n#42 รายการจ่าย\n&lt;b&gt;ข้าว&lt;/b&gt; &amp; น้ำ : 10 บาท", text);
    }

    [Fact]
    public void Category_is_not_shown()
    {
        var text = _formatter.Format(Tx(TransactionType.Expense, 5m, "x", "missing"));
        Assert.Equal("🔴\n#42 รายการจ่าย\nx : 5 บาท", text);
    }

    [Fact]
    public void Empty_description_shows_only_the_amount()
    {
        var text = _formatter.Format(Tx(TransactionType.Expense, 60m, "", DefaultCategories.ExpenseOther));
        Assert.Equal("🔴\n#42 รายการจ่าย\n60 บาท", text);
    }

    [Fact]
    public void Deleted_and_edited_prefixes()
    {
        var tx = Tx(TransactionType.Expense, 600m, "ซื้อข้าวกลางวัน", "food");
        Assert.Equal("ลบแล้ว\n🔴\n#42 รายการจ่าย\nซื้อข้าวกลางวัน : 600 บาท", _formatter.Deleted(tx));
        Assert.Equal("แก้แล้ว\n🔴\n#42 รายการจ่าย\nซื้อข้าวกลางวัน : 600 บาท", _formatter.Edited(tx));
    }

    [Fact]
    public void Summary_by_category_with_balance()
    {
        var period = SummaryPeriod.Today(new DateTime(2026, 11, 21, 5, 0, 0, DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok"));
        var report = new SummaryReport(period,
        [
            new CategoryTotal(TransactionType.Expense, "food", 180m, 3),
            new CategoryTotal(TransactionType.Income, "salary", 25000m, 1),
            new CategoryTotal(TransactionType.Expense, "weird", 1000.5m, 1),
        ]);

        Assert.Equal(
            "<b>สรุปวันนี้ (21 พ.ย. 2026)</b>\n\n" +
            "<b>รายรับ</b> +25,000 บาท\n• เงินเดือน +25,000\n\n" +
            "<b>รายจ่าย</b> -1,180.50 บาท\n• อาหาร -180\n• A&amp;B &lt;x&gt; -1,000.50\n\n" +
            "🟢 <b>คงเหลือ</b> +23,819.50 บาท",
            _formatter.Summary(report));
    }

    [Fact]
    public void Summary_negative_balance_is_red()
    {
        var period = SummaryPeriod.Month(2026, 10, TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok"));
        var report = new SummaryReport(period, [new CategoryTotal(TransactionType.Expense, "food", 60m, 1)]);

        var text = _formatter.Summary(report);
        Assert.StartsWith("<b>สรุปเดือนตุลาคม 2026</b>", text);
        Assert.Contains("<b>รายรับ</b> 0 บาท", text);
        Assert.EndsWith("🔴 <b>คงเหลือ</b> -60 บาท", text);
    }

    [Theory]
    [InlineData(2026, 11, 16, 2026, 11, 22, "16–22 พ.ย. 2026")]
    [InlineData(2026, 11, 30, 2026, 12, 6, "30 พ.ย. – 6 ธ.ค. 2026")]
    [InlineData(2026, 12, 28, 2027, 1, 3, "28 ธ.ค. 2026 – 3 ม.ค. 2027")]
    public void Thai_date_range(int y1, int m1, int d1, int y2, int m2, int d2, string expected)
    {
        Assert.Equal(expected, TransactionFormatter.ThaiDateRange(new DateOnly(y1, m1, d1), new DateOnly(y2, m2, d2)));
    }

    [Fact]
    public void User_text_in_admin_messages_is_escaped()
    {
        var user = new BotUser { Id = 5, DisplayName = "<script>", Username = "a&b" };
        Assert.Equal("<b>คำขอใช้งานใหม่</b>\n&lt;script&gt; (@a&amp;b) ขอใช้งาน\nID: <code>5</code>",
            _formatter.AccessRequestForAdmin(user));
    }
}
