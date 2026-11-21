using Telegram.Bot.Types.Enums;

namespace ExpenseBot.Infrastructure.Telegram;

public static class TelegramUpdateTypes
{
    /// <summary>The update types the bot handles; used by both getUpdates and setWebhook.</summary>
    public static readonly UpdateType[] Allowed =
        [UpdateType.Message, UpdateType.EditedMessage, UpdateType.CallbackQuery, UpdateType.MyChatMember];
}
