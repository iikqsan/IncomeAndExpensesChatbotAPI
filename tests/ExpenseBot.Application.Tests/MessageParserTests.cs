using ExpenseBot.Application.Domain;
using ExpenseBot.Application.Parsing;

namespace ExpenseBot.Application.Tests;

public class MessageParserTests
{
    [Theory]
    [InlineData("ซื้อข้าวกลางวัน 60", "60", "ซื้อข้าวกลางวัน")]
    [InlineData("ข้าว 2 จาน 120", "120", "ข้าว 2 จาน")]
    [InlineData("ค่าเช่า 1,200", "1200", "ค่าเช่า")]
    [InlineData("กาแฟ 45.50", "45.50", "กาแฟ")]
    [InlineData("กาแฟ 45.5", "45.5", "กาแฟ")]
    [InlineData("ทีวี 12,500.75", "12500.75", "ทีวี")]
    [InlineData("  ข้าวมันไก่   50  ", "50", "ข้าวมันไก่")]
    [InlineData("ข้าว 60 บาท", "60", "ข้าว")]
    [InlineData("60 ค่าแท็กซี่", "60", "ค่าแท็กซี่")]
    [InlineData("ข้าว60", "60", "ข้าว")]
    public void Parses_expense(string text, string expectedAmount, string expectedDescription)
    {
        var r = MessageParser.Parse(text);

        Assert.True(r.Success);
        Assert.Equal(TransactionType.Expense, r.Type);
        Assert.Equal(decimal.Parse(expectedAmount, System.Globalization.CultureInfo.InvariantCulture), r.Amount);
        Assert.Equal(expectedDescription, r.Description);
        Assert.Equal(text, r.RawText);
    }

    [Fact]
    public void Decimal_amount_keeps_scale()
    {
        Assert.Equal("45.50", MessageParser.Parse("กาแฟ 45.50").Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Plus_prefix_is_income_and_plus_is_removed_from_description()
    {
        var r = MessageParser.Parse("+เงินเดือน 25000");

        Assert.True(r.Success);
        Assert.Equal(TransactionType.Income, r.Type);
        Assert.Equal(25000m, r.Amount);
        Assert.Equal("เงินเดือน", r.Description);
        Assert.Equal("+เงินเดือน 25000", r.RawText);
    }

    [Theory]
    [InlineData("โบนัส 5000", 5000)]
    [InlineData("เงินเดือนพฤศจิกายน 25,000", 25000)]
    [InlineData("ได้เงินจากแม่ 500", 500)]
    [InlineData("รับค่าขนม 100", 100)]
    [InlineData("+ ขายของ 300", 300)]
    public void Income_keywords_mark_income(string text, decimal amount)
    {
        var r = MessageParser.Parse(text);

        Assert.True(r.Success);
        Assert.Equal(TransactionType.Income, r.Type);
        Assert.Equal(amount, r.Amount);
    }

    [Theory]
    [InlineData("สวัสดี")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("ข้าว 0")]
    [InlineData("ข้าว 0.00")]
    [InlineData("ข้าว -50")]
    [InlineData("+เงินเดือน -100")]
    [InlineData("ข้าว 12,00")]
    [InlineData("กาแฟ 45.505")]
    public void Unparsable(string? text)
    {
        Assert.False(MessageParser.Parse(text).Success);
    }
}
