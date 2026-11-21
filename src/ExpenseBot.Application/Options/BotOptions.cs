namespace ExpenseBot.Application.Options;

public sealed class BotOptions
{
    public const string SectionName = "Bot";

    public long AdminUserId { get; set; }
    public string DefaultTimezone { get; set; } = "Asia/Bangkok";
}
