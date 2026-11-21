using ExpenseBot.Application.Abstractions;
using ExpenseBot.Application.Updates;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ExpenseBot.Infrastructure.Telegram;

public enum TelegramMode
{
    Polling,
    Webhook,
}

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public string BotToken { get; set; } = "";
    public TelegramMode Mode { get; set; } = TelegramMode.Polling;
    public string? WebhookUrl { get; set; }
    public string? WebhookSecret { get; set; }
}

internal sealed class TelegramBotMessenger(ITelegramBotClient bot) : IBotMessenger
{
    public Task SendHtmlAsync(long chatId, string html, CancellationToken ct, IReadOnlyList<InlineButton>? buttons = null)
    {
        var markup = buttons is { Count: > 0 }
            ? new InlineKeyboardMarkup(buttons.Select(b => InlineKeyboardButton.WithCallbackData(b.Text, b.CallbackData)))
            : null;
        return bot.SendMessage(chatId, html, parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ct);
    }

    public Task EditHtmlAsync(long chatId, long messageId, string html, CancellationToken ct) =>
        bot.EditMessageText(chatId, checked((int)messageId), html, parseMode: ParseMode.Html, cancellationToken: ct);

    public Task AnswerCallbackAsync(string callbackQueryId, string? text, CancellationToken ct) =>
        bot.AnswerCallbackQuery(callbackQueryId, text, cancellationToken: ct);

    public Task LeaveChatAsync(long chatId, CancellationToken ct) =>
        bot.LeaveChat(chatId, cancellationToken: ct);
}

/// <summary>Maps Telegram.Bot's <see cref="Update"/> into the Application's <see cref="BotUpdate"/>.</summary>
public static class TelegramUpdateMapper
{
    public static BotUpdate Map(Update update)
    {
        if ((update.Message ?? update.EditedMessage) is { } message)
        {
            return new BotUpdate
            {
                UpdateId = update.Id,
                Kind = update.Message is not null ? BotUpdateKind.Message : BotUpdateKind.EditedMessage,
                ChatId = message.Chat.Id,
                Chat = ToChatKind(message.Chat.Type),
                From = ToSender(message.From),
                MessageId = message.Id,
                Text = message.Text,
                DateUtc = DateTime.SpecifyKind(message.Date, DateTimeKind.Utc),
            };
        }

        if (update.CallbackQuery is { } cb)
        {
            return new BotUpdate
            {
                UpdateId = update.Id,
                Kind = BotUpdateKind.CallbackQuery,
                ChatId = cb.Message?.Chat.Id ?? cb.From.Id,
                Chat = cb.Message is null ? ChatKind.Private : ToChatKind(cb.Message.Chat.Type),
                From = ToSender(cb.From),
                MessageId = cb.Message?.Id ?? 0,
                Text = cb.Data,
                CallbackQueryId = cb.Id,
            };
        }

        if (update.MyChatMember is { } member)
        {
            return new BotUpdate
            {
                UpdateId = update.Id,
                Kind = BotUpdateKind.MyChatMember,
                ChatId = member.Chat.Id,
                Chat = ToChatKind(member.Chat.Type),
                From = ToSender(member.From),
                BotIsMember = member.NewChatMember.Status is ChatMemberStatus.Member
                    or ChatMemberStatus.Administrator or ChatMemberStatus.Restricted,
                DateUtc = DateTime.SpecifyKind(member.Date, DateTimeKind.Utc),
            };
        }

        return new BotUpdate { UpdateId = update.Id, Kind = BotUpdateKind.Other };
    }

    private static ChatKind ToChatKind(ChatType type) => type switch
    {
        ChatType.Private => ChatKind.Private,
        ChatType.Group or ChatType.Supergroup => ChatKind.Group,
        ChatType.Channel => ChatKind.Channel,
        _ => ChatKind.Unknown,
    };

    private static BotSender? ToSender(User? u) =>
        u is null ? null : new BotSender(u.Id, u.Username, u.FirstName, u.LastName);
}
