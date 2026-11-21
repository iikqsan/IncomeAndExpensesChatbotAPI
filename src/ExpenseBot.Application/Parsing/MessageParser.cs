using System.Globalization;
using System.Text.RegularExpressions;
using ExpenseBot.Application.Domain;

namespace ExpenseBot.Application.Parsing;

public sealed record ParseResult
{
    public bool Success { get; private init; }
    public TransactionType Type { get; private init; }
    public decimal Amount { get; private init; }
    public string Description { get; private init; } = "";
    public string RawText { get; private init; } = "";

    public static ParseResult Ok(TransactionType type, decimal amount, string description, string rawText) =>
        new() { Success = true, Type = type, Amount = amount, Description = description, RawText = rawText };

    public static ParseResult Fail(string rawText) => new() { Success = false, RawText = rawText };
}

/// <summary>
/// Parses free text such as "ซื้อข้าวกลางวัน 60" into amount, description and type.
/// Pure function: no I/O, no clock.
/// </summary>
public static partial class MessageParser
{
    private static readonly string[] IncomeKeywords = ["รับ", "เงินเดือน", "ได้เงิน", "โบนัส"];

    // A number token, optionally preceded by '-' so negative amounts can be detected and rejected.
    // Commas and the decimal part are matched loosely here and validated by ValidAmount.
    [GeneratedRegex(@"-?\d[\d,]*(?:\.\d+)?")]
    private static partial Regex NumberToken();

    // "60", "1200", "1,200", "45.5", "45.50" (at most 2 decimal places, proper thousand groups).
    [GeneratedRegex(@"^(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d{1,2})?$")]
    private static partial Regex ValidAmount();

    // Optional currency word right after the amount, e.g. "ข้าว 60 บาท".
    [GeneratedRegex(@"^\s*(?:บาท|บ\.)")]
    private static partial Regex CurrencySuffix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static ParseResult Parse(string? text)
    {
        var raw = text ?? "";
        var body = raw.Trim();
        if (body.Length == 0)
            return ParseResult.Fail(raw);

        var explicitIncome = body.StartsWith('+');
        if (explicitIncome)
            body = body[1..].TrimStart();

        var matches = NumberToken().Matches(body);
        if (matches.Count == 0)
            return ParseResult.Fail(raw);

        // Amount = the last number in the message ("ข้าว 2 จาน 120" -> 120).
        var last = matches[^1];
        var token = last.Value.TrimEnd(',');
        if (token.StartsWith('-') || !ValidAmount().IsMatch(token))
            return ParseResult.Fail(raw);

        var amount = decimal.Parse(token.Replace(",", ""), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        if (amount <= 0)
            return ParseResult.Fail(raw);

        var before = body[..last.Index];
        var after = body[(last.Index + last.Length)..];
        var suffix = CurrencySuffix().Match(after);
        if (suffix.Success)
            after = after[suffix.Length..];

        var description = Whitespace().Replace($"{before} {after}", " ").Trim();

        var type = explicitIncome || IncomeKeywords.Any(k => description.Contains(k, StringComparison.Ordinal))
            ? TransactionType.Income
            : TransactionType.Expense;

        return ParseResult.Ok(type, amount, description, raw);
    }
}
