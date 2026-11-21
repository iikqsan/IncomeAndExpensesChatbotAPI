using ExpenseBot.Application.Abstractions;
using ExpenseBot.Application.Categories;
using ExpenseBot.Application.Domain;
using ExpenseBot.Application.Parsing;

namespace ExpenseBot.Application.Transactions;

public enum RecordOutcome
{
    Recorded,
    ParseFailed,
    Duplicate,
}

public enum EditOutcome
{
    Updated,
    ParseFailed,
    NotFound,
}

public sealed record RecordResult(RecordOutcome Outcome, Transaction? Transaction = null);

public sealed record EditResult(EditOutcome Outcome, Transaction? Transaction = null);

/// <summary>Create, edit and (soft) delete transactions. A user can only ever touch their own.</summary>
public sealed class TransactionService(
    ITransactionRepository transactions,
    IUserRepository users,
    ICategoryProvider categories,
    TimeProvider clock)
{
    public const int ListSize = 10;

    public async Task<RecordResult> RecordAsync(long userId, long messageId, string text, DateTime occurredAtUtc, CancellationToken ct)
    {
        var parsed = MessageParser.Parse(text);
        if (!parsed.Success)
            return new RecordResult(RecordOutcome.ParseFailed);

        var now = Now;
        var tx = new Transaction
        {
            UserId = userId,
            Seq = await users.NextSeqAsync(userId, ct),
            MessageId = messageId,
            CreatedAt = now,
            OccurredAt = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc),
        };
        Apply(tx, parsed, now);

        return await transactions.TryInsertAsync(userId, tx, ct)
            ? new RecordResult(RecordOutcome.Recorded, tx)
            : new RecordResult(RecordOutcome.Duplicate);
    }

    /// <summary>
    /// Re-parses an edited Telegram message. If the new text cannot be parsed the stored
    /// transaction is left unchanged and returned with <see cref="EditOutcome.ParseFailed"/>.
    /// </summary>
    public async Task<EditResult> EditFromMessageAsync(long userId, long messageId, string text, CancellationToken ct)
    {
        var tx = await transactions.GetByMessageIdAsync(userId, messageId, ct);
        if (tx is null)
            return new EditResult(EditOutcome.NotFound);

        var parsed = MessageParser.Parse(text);
        if (!parsed.Success)
            return new EditResult(EditOutcome.ParseFailed, tx);

        Apply(tx, parsed, Now);
        return await transactions.UpdateParsedFieldsAsync(userId, tx, ct)
            ? new EditResult(EditOutcome.Updated, tx)
            : new EditResult(EditOutcome.NotFound);
    }

    /// <summary>Soft-deletes the user's most recent transaction.</summary>
    public async Task<Transaction?> UndoAsync(long userId, CancellationToken ct)
    {
        var latest = await transactions.GetLatestAsync(userId, 1, ct);
        return latest.Count == 0 ? null : await DeleteAsync(userId, latest[0], ct);
    }

    public async Task<Transaction?> DeleteBySeqAsync(long userId, long seq, CancellationToken ct)
    {
        var tx = await transactions.GetBySeqAsync(userId, seq, ct);
        return tx is null ? null : await DeleteAsync(userId, tx, ct);
    }

    /// <summary>The latest <see cref="ListSize"/> transactions, oldest first (newest at the bottom of the chat).</summary>
    public async Task<IReadOnlyList<Transaction>> ListRecentAsync(long userId, CancellationToken ct)
    {
        var latest = await transactions.GetLatestAsync(userId, ListSize, ct);
        return latest.Reverse().ToList();
    }

    private async Task<Transaction?> DeleteAsync(long userId, Transaction tx, CancellationToken ct)
    {
        var now = Now;
        if (!await transactions.SoftDeleteAsync(userId, tx.Id!, now, ct))
            return null;
        tx.IsDeleted = true;
        tx.UpdatedAt = now;
        return tx;
    }

    private void Apply(Transaction tx, ParseResult parsed, DateTime now)
    {
        tx.Type = parsed.Type;
        tx.Amount = parsed.Amount;
        tx.Description = parsed.Description;
        tx.RawText = parsed.RawText;
        tx.CategoryId = CategoryClassifier.Classify(parsed.Description, parsed.Type, categories.GetAll());
        tx.UpdatedAt = now;
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
