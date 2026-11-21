using ExpenseBot.Application.Domain;

namespace ExpenseBot.Application.Abstractions;

// Rule: every method that touches a user's data takes userId and every such query filters by it.
// Household-wide data (categories) and the admin's user listing are the only exceptions.

public interface IUserRepository
{
    /// <summary>
    /// Creates the user if missing (role/status/timezone taken from <paramref name="defaults"/>),
    /// always refreshes username and display name, and returns the stored user.
    /// </summary>
    Task<BotUser> UpsertProfileAsync(long userId, string? username, string displayName, BotUser defaults, CancellationToken ct);

    Task<BotUser?> GetAsync(long userId, CancellationToken ct);

    /// <summary>Atomically increments <c>nextSeq</c> (<c>$inc</c>) and returns the new value.</summary>
    Task<long> NextSeqAsync(long userId, CancellationToken ct);

    /// <summary>Sets the status. Returns false if the user does not exist.</summary>
    Task<bool> SetStatusAsync(long userId, UserStatus status, CancellationToken ct);

    Task<BotUser> SetAccessAsync(long userId, UserRole role, UserStatus status, CancellationToken ct);

    /// <summary>Admin only: every user (profile and status, never financial data).</summary>
    Task<IReadOnlyList<BotUser>> ListAllAsync(CancellationToken ct);
}

public sealed record CategoryTotal(TransactionType Type, string CategoryId, decimal Total, int Count);

public interface ITransactionRepository
{
    /// <summary>
    /// Inserts the transaction. Returns false if one with the same <c>{userId, messageId}</c> already exists.
    /// </summary>
    Task<bool> TryInsertAsync(long userId, Transaction transaction, CancellationToken ct);

    Task<Transaction?> GetByMessageIdAsync(long userId, long messageId, CancellationToken ct);

    Task<Transaction?> GetBySeqAsync(long userId, long seq, CancellationToken ct);

    /// <summary>Latest transactions, newest first (by seq).</summary>
    Task<IReadOnlyList<Transaction>> GetLatestAsync(long userId, int limit, CancellationToken ct);

    /// <summary>Updates type/amount/category/description/rawText/updatedAt of a non-deleted transaction.</summary>
    Task<bool> UpdateParsedFieldsAsync(long userId, Transaction transaction, CancellationToken ct);

    /// <summary>Sets <c>isDeleted = true</c>. Returns false if not found or already deleted.</summary>
    Task<bool> SoftDeleteAsync(long userId, string transactionId, DateTime nowUtc, CancellationToken ct);

    /// <summary>Totals per type and category for <c>[fromUtc, toUtc)</c>.</summary>
    Task<IReadOnlyList<CategoryTotal>> SummarizeAsync(long userId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}

public interface IProcessedUpdateRepository
{
    /// <summary>Records the update. Returns false if it was already processed.</summary>
    Task<bool> TryMarkProcessedAsync(long userId, long updateId, CancellationToken ct);
}

/// <summary>Household-wide categories (not per user).</summary>
public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct);

    /// <summary>Inserts <paramref name="categories"/> only when the collection is empty. Returns true if seeded.</summary>
    Task<bool> SeedIfEmptyAsync(IEnumerable<Category> categories, CancellationToken ct);

    /// <summary>Adds the keyword (<c>$addToSet</c>). Returns false if it was already there.</summary>
    Task<bool> AddKeywordAsync(string categoryId, string keyword, CancellationToken ct);
}

/// <summary>Synchronous read access to the cached categories.</summary>
public interface ICategoryProvider
{
    IReadOnlyList<Category> GetAll();
    Category? GetById(string id);
}

public sealed record InlineButton(string Text, string CallbackData);

/// <summary>Outgoing calls to Telegram. Text is always sent with parse mode HTML.</summary>
public interface IBotMessenger
{
    Task SendHtmlAsync(long chatId, string html, CancellationToken ct, IReadOnlyList<InlineButton>? buttons = null);

    /// <summary>Replaces the text of a message the bot sent (and removes its inline keyboard).</summary>
    Task EditHtmlAsync(long chatId, long messageId, string html, CancellationToken ct);

    /// <summary>Answers a callback query. <paramref name="text"/> is plain text (shown as a toast).</summary>
    Task AnswerCallbackAsync(string callbackQueryId, string? text, CancellationToken ct);

    Task LeaveChatAsync(long chatId, CancellationToken ct);
}
