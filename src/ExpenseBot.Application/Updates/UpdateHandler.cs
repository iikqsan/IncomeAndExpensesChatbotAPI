using ExpenseBot.Application.Abstractions;
using ExpenseBot.Application.Categories;
using ExpenseBot.Application.Domain;
using ExpenseBot.Application.Formatting;
using ExpenseBot.Application.Transactions;
using ExpenseBot.Application.Users;
using Microsoft.Extensions.Logging;

namespace ExpenseBot.Application.Updates;

/// <summary>
/// Entry point for every update, regardless of Polling or Webhook.
/// Gates, in order: private chat → not yet processed → user active (except /start) → route.
/// </summary>
public sealed class UpdateHandler(
    UserService userService,
    TransactionService transactionService,
    CommandHandler commands,
    CategoryCache categoryCache,
    IProcessedUpdateRepository processedUpdates,
    IBotMessenger messenger,
    TransactionFormatter formatter,
    ILogger<UpdateHandler> logger)
{
    public const string ApprovePrefix = "approve:";
    public const string RejectPrefix = "reject:";

    public async Task HandleAsync(BotUpdate update, CancellationToken ct)
    {
        // 1. Private chats only. If someone added the bot to a group/channel, leave it.
        if (!update.IsPrivateChat)
        {
            await LeaveIfInGroupAsync(update, ct);
            return;
        }

        if (update.From is null || update.Kind is BotUpdateKind.Other or BotUpdateKind.MyChatMember)
            return;

        var userId = update.From.Id;

        // 2. Skip duplicates (Telegram may redeliver).
        if (!await processedUpdates.TryMarkProcessedAsync(userId, update.UpdateId, ct))
        {
            logger.LogInformation("Skipping duplicate update {UpdateId}", update.UpdateId);
            return;
        }

        await categoryCache.EnsureLoadedAsync(ct);

        // Every contact refreshes username/displayName.
        var user = await userService.EnsureUserAsync(update.From, ct);

        // 3. Non-active users may only /start.
        if (user.Status != UserStatus.Active)
        {
            await HandleInactiveAsync(user, update, ct);
            return;
        }

        // 4. Route.
        switch (update.Kind)
        {
            case BotUpdateKind.Message when update.Text is { } text && text.StartsWith('/'):
                await commands.HandleAsync(user, update.ChatId, text, ct);
                break;

            case BotUpdateKind.Message when update.Text is { } text:
                await HandleNewTransactionAsync(user, update, text, ct);
                break;

            case BotUpdateKind.EditedMessage when update.Text is { } text && !text.StartsWith('/'):
                await HandleEditAsync(user, update, text, ct);
                break;

            case BotUpdateKind.CallbackQuery:
                await HandleApprovalAsync(user, update, ct);
                break;
        }
    }

    private async Task LeaveIfInGroupAsync(BotUpdate update, CancellationToken ct)
    {
        if (update.Chat is not (ChatKind.Group or ChatKind.Channel) || update.ChatId == 0)
            return;
        // A my_chat_member saying we were removed needs no action.
        if (update.Kind == BotUpdateKind.MyChatMember && !update.BotIsMember)
            return;

        logger.LogInformation("Leaving non-private chat {ChatId}", update.ChatId);
        try
        {
            await messenger.LeaveChatAsync(update.ChatId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not leave chat {ChatId}", update.ChatId);
        }
    }

    private async Task HandleInactiveAsync(BotUser user, BotUpdate update, CancellationToken ct)
    {
        switch (update.Kind)
        {
            case BotUpdateKind.Message when user.Status == UserStatus.Pending && IsStart(update.Text):
                await messenger.SendHtmlAsync(update.ChatId, formatter.AccessRequested(), ct);
                await NotifyAdminAsync(user, ct);
                break;

            case BotUpdateKind.Message:
                await messenger.SendHtmlAsync(update.ChatId,
                    user.Status == UserStatus.Blocked ? formatter.Blocked() : formatter.Pending(), ct);
                break;

            case BotUpdateKind.CallbackQuery when update.CallbackQueryId is { } id:
                await messenger.AnswerCallbackAsync(id, formatter.CallbackNotAllowedPlain(), ct);
                break;
        }
    }

    private async Task NotifyAdminAsync(BotUser user, CancellationToken ct)
    {
        if (userService.AdminUserId == 0)
        {
            logger.LogWarning("Bot:AdminUserId is not configured; cannot forward access request of {UserId}", user.Id);
            return;
        }

        await messenger.SendHtmlAsync(userService.AdminUserId, formatter.AccessRequestForAdmin(user), ct,
        [
            new InlineButton(formatter.ApproveButton(), ApprovePrefix + user.Id),
            new InlineButton(formatter.RejectButton(), RejectPrefix + user.Id),
        ]);
    }

    private async Task HandleNewTransactionAsync(BotUser user, BotUpdate update, string text, CancellationToken ct)
    {
        var result = await transactionService.RecordAsync(user.Id, update.MessageId, text, update.DateUtc, ct);
        switch (result.Outcome)
        {
            case RecordOutcome.Recorded:
                await messenger.SendHtmlAsync(update.ChatId, formatter.Format(result.Transaction!), ct);
                break;
            case RecordOutcome.ParseFailed:
                logger.LogInformation("Unparsable message from {UserId}: {Text}", user.Id, text);
                await messenger.SendHtmlAsync(update.ChatId, formatter.ParseFailed(), ct);
                break;
            case RecordOutcome.Duplicate:
                logger.LogInformation("Message {MessageId} of {UserId} already recorded", update.MessageId, user.Id);
                break;
        }
    }

    private async Task HandleEditAsync(BotUser user, BotUpdate update, string text, CancellationToken ct)
    {
        var result = await transactionService.EditFromMessageAsync(user.Id, update.MessageId, text, ct);
        switch (result.Outcome)
        {
            case EditOutcome.Updated:
                await messenger.SendHtmlAsync(update.ChatId, formatter.Edited(result.Transaction!), ct);
                break;
            case EditOutcome.ParseFailed:
                logger.LogInformation("Unparsable edit from {UserId}: {Text}", user.Id, text);
                await messenger.SendHtmlAsync(update.ChatId, formatter.EditParseFailed(result.Transaction!), ct);
                break;
            case EditOutcome.NotFound:
                // The original message was never recorded (e.g. it had no amount): treat the edit as a new entry.
                // If it was recorded and later deleted, the unique {userId, messageId} index makes this a no-op.
                await HandleNewTransactionAsync(user, update, text, ct);
                break;
        }
    }

    private async Task HandleApprovalAsync(BotUser admin, BotUpdate update, CancellationToken ct)
    {
        var callbackId = update.CallbackQueryId!;
        if (admin.Role != UserRole.Admin)
        {
            await messenger.AnswerCallbackAsync(callbackId, formatter.CallbackNotAllowedPlain(), ct);
            return;
        }

        var data = update.Text ?? "";
        bool approve;
        string idText;
        if (data.StartsWith(ApprovePrefix, StringComparison.Ordinal))
            (approve, idText) = (true, data[ApprovePrefix.Length..]);
        else if (data.StartsWith(RejectPrefix, StringComparison.Ordinal))
            (approve, idText) = (false, data[RejectPrefix.Length..]);
        else
        {
            await messenger.AnswerCallbackAsync(callbackId, null, ct);
            return;
        }

        if (!long.TryParse(idText, out var targetId))
        {
            await messenger.AnswerCallbackAsync(callbackId, formatter.CallbackUserNotFoundPlain(), ct);
            return;
        }

        var result = await userService.DecideAsync(targetId, approve, ct);
        switch (result.Outcome)
        {
            case DecisionOutcome.Done:
                var target = result.User!;
                await messenger.AnswerCallbackAsync(callbackId, formatter.CallbackDonePlain(approve), ct);
                if (update.MessageId != 0)
                    await messenger.EditHtmlAsync(update.ChatId, update.MessageId, formatter.AdminDecisionDone(target, approve), ct);
                await messenger.SendHtmlAsync(target.Id,
                    approve ? formatter.ApprovedNotice() : formatter.RejectedNotice(), ct);
                break;
            case DecisionOutcome.AlreadyDecided:
                await messenger.AnswerCallbackAsync(callbackId, formatter.CallbackAlreadyDecidedPlain(), ct);
                break;
            default:
                await messenger.AnswerCallbackAsync(callbackId, formatter.CallbackUserNotFoundPlain(), ct);
                break;
        }
    }

    private static bool IsStart(string? text) => CommandHandler.TryParse(text, out var name, out _) && name == "start";
}
