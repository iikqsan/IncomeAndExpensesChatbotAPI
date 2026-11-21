namespace ExpenseBot.Application.Updates;

public enum BotUpdateKind
{
    Message,
    EditedMessage,
    CallbackQuery,

    /// <summary>The bot's own membership in a chat changed (<c>my_chat_member</c>).</summary>
    MyChatMember,
    Other,
}

public enum ChatKind
{
    Unknown,
    Private,
    Group,
    Channel,
}

public sealed record BotSender(long Id, string? Username, string? FirstName, string? LastName)
{
    public string DisplayName =>
        string.Join(' ', new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } name
            ? name
            : Username ?? Id.ToString();
}

/// <summary>
/// Transport-neutral view of a Telegram update. Polling and Webhook both map
/// Telegram's <c>Update</c> into this and pass it to the same <see cref="UpdateHandler"/>.
/// </summary>
public sealed record BotUpdate
{
    public long UpdateId { get; init; }
    public BotUpdateKind Kind { get; init; }
    public long ChatId { get; init; }
    public ChatKind Chat { get; init; }
    public bool IsPrivateChat => Chat == ChatKind.Private;
    public BotSender? From { get; init; }
    public long MessageId { get; init; }

    /// <summary>Message text, or callback data for <see cref="BotUpdateKind.CallbackQuery"/>.</summary>
    public string? Text { get; init; }

    public string? CallbackQueryId { get; init; }

    /// <summary>For <see cref="BotUpdateKind.MyChatMember"/>: the bot is now a member/admin of the chat.</summary>
    public bool BotIsMember { get; init; }

    /// <summary>Telegram <c>message.date</c>, UTC.</summary>
    public DateTime DateUtc { get; init; }
}
