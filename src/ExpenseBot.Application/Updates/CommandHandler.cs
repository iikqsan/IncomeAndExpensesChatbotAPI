using System.Globalization;
using System.Text.RegularExpressions;
using ExpenseBot.Application.Abstractions;
using ExpenseBot.Application.Categories;
using ExpenseBot.Application.Domain;
using ExpenseBot.Application.Formatting;
using ExpenseBot.Application.Summaries;
using ExpenseBot.Application.Transactions;
using ExpenseBot.Application.Users;

namespace ExpenseBot.Application.Updates;

/// <summary>Handles "/..." commands of an active user. Every summary/list only ever reads the caller's own data.</summary>
public sealed partial class CommandHandler(
    TransactionService transactionService,
    SummaryService summaryService,
    UserService userService,
    ICategoryRepository categoryRepository,
    CategoryCache categoryCache,
    IBotMessenger messenger,
    TransactionFormatter formatter)
{
    [GeneratedRegex(@"^/(?<name>[A-Za-z0-9_]+)(?:@\S+)?(?:\s+(?<args>.*))?$", RegexOptions.Singleline)]
    private static partial Regex CommandPattern();

    [GeneratedRegex(@"^(?<y>\d{4})-(?<m>\d{1,2})$")]
    private static partial Regex MonthArg();

    /// <summary>Splits "/cmd@BotName arg1 arg2" into a lowercase name and the trimmed argument string.</summary>
    public static bool TryParse(string? text, out string name, out string args)
    {
        var m = CommandPattern().Match((text ?? "").Trim());
        name = m.Success ? m.Groups["name"].Value.ToLowerInvariant() : "";
        args = m.Success ? m.Groups["args"].Value.Trim() : "";
        return m.Success;
    }

    public async Task HandleAsync(BotUser user, long chatId, string text, CancellationToken ct)
    {
        if (!TryParse(text, out var name, out var args))
        {
            await Reply(formatter.UnknownCommand());
            return;
        }

        var isAdmin = user.Role == UserRole.Admin;
        var reply = name switch
        {
            "start" => formatter.Welcome(user),
            "help" => formatter.Help(isAdmin),
            "today" => formatter.Summary(await summaryService.TodayAsync(user, ct)),
            "week" => formatter.Summary(await summaryService.ThisWeekAsync(user, ct)),
            "month" => await MonthAsync(user, args, ct),
            "list" => formatter.List(await transactionService.ListRecentAsync(user.Id, ct)),
            "undo" => await UndoAsync(user, ct),
            "delete" => await DeleteAsync(user, args, ct),
            "users" when isAdmin => formatter.Users(await userService.ListAsync(ct)),
            "block" when isAdmin => await BlockAsync(args, ct),
            "unblock" when isAdmin => await UnblockAsync(args, ct),
            "addkeyword" when isAdmin => await AddKeywordAsync(args, ct),
            "users" or "block" or "unblock" or "addkeyword" => formatter.NotAdmin(),
            _ => formatter.UnknownCommand(),
        };
        await Reply(reply);

        Task Reply(string html) => messenger.SendHtmlAsync(chatId, html, ct);
    }

    private async Task<string> MonthAsync(BotUser user, string args, CancellationToken ct)
    {
        if (args.Length == 0)
            return formatter.MonthSummary(await summaryService.ThisMonthAsync(user, ct));

        var m = MonthArg().Match(args);
        if (!m.Success)
            return formatter.Usage("/month หรือ /month 2026-10");

        var year = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
        if (month is < 1 or > 12 || year < 2000)
            return formatter.Usage("/month หรือ /month 2026-10");

        return formatter.MonthSummary(await summaryService.MonthAsync(user, year, month, ct));
    }

    private async Task<string> UndoAsync(BotUser user, CancellationToken ct)
    {
        var deleted = await transactionService.UndoAsync(user.Id, ct);
        return deleted is null ? formatter.NothingToUndo() : formatter.Deleted(deleted);
    }

    private async Task<string> DeleteAsync(BotUser user, string args, CancellationToken ct)
    {
        if (!long.TryParse(args.TrimStart('#'), NumberStyles.None, CultureInfo.InvariantCulture, out var seq) || seq <= 0)
            return formatter.Usage("/delete <#> เช่น /delete 42");

        var deleted = await transactionService.DeleteBySeqAsync(user.Id, seq, ct);
        return deleted is null ? formatter.SeqNotFound(seq) : formatter.Deleted(deleted);
    }

    private async Task<string> BlockAsync(string args, CancellationToken ct)
    {
        if (!long.TryParse(args, NumberStyles.None, CultureInfo.InvariantCulture, out var targetId))
            return formatter.Usage("/block <id> (ดู id ได้จาก /users)");

        if (userService.IsConfiguredAdmin(targetId))
            return formatter.CannotBlockAdmin();

        var result = await userService.BlockAsync(targetId, ct);
        return result.Outcome switch
        {
            DecisionOutcome.Done => formatter.UserBlocked(result.User!),
            DecisionOutcome.AlreadyDecided => formatter.UserAlreadyBlocked(result.User!),
            _ => formatter.UserNotFound(targetId),
        };
    }

    private async Task<string> UnblockAsync(string args, CancellationToken ct)
    {
        if (!long.TryParse(args, NumberStyles.None, CultureInfo.InvariantCulture, out var targetId))
            return formatter.Usage("/unblock <id> (ดู id ได้จาก /users)");

        var result = await userService.UnblockAsync(targetId, ct);
        switch (result.Outcome)
        {
            case DecisionOutcome.Done:
                await messenger.SendHtmlAsync(targetId, formatter.UnblockedNotice(), ct);
                return formatter.UserUnblocked(result.User!);
            case DecisionOutcome.AlreadyDecided:
                return formatter.UserNotBlocked(result.User!);
            default:
                return formatter.UserNotFound(targetId);
        }
    }

    private async Task<string> AddKeywordAsync(string args, CancellationToken ct)
    {
        var parts = args.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return formatter.Usage("/addkeyword <หมวด> <คำ> เช่น /addkeyword อาหาร ข้าวมันไก่");

        var (categoryName, keyword) = (parts[0], parts[1].ToLowerInvariant());

        // "อื่นๆ" exists for both types and is the fallback, so keywords there make no sense.
        var category = categoryCache.GetAll()
            .FirstOrDefault(c => !DefaultCategories.IsOther(c.Id) && c.Name == categoryName);
        if (category is null)
            return formatter.CategoryNotFound(categoryName);

        if (!await categoryRepository.AddKeywordAsync(category.Id, keyword, ct))
            return formatter.KeywordExists(category, keyword);

        await categoryCache.ReloadAsync(ct);
        return formatter.KeywordAdded(category, keyword);
    }
}
