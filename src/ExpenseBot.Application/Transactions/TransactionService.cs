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

    private const int MaxSeqAttempts = 5;

    public async Task<RecordResult> RecordAsync(long userId, long messageId, string text, DateTime occurredAtUtc, CancellationToken ct)
    {
        var parsed = MessageParser.Parse(text);
        if (!parsed.Success)
            return new RecordResult(RecordOutcome.ParseFailed);

        var now = Now;
        var tx = new Transaction
        {
            UserId = userId,
            MessageId = messageId,
            CreatedAt = now,
            OccurredAt = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc),
        };
        Apply(tx, parsed, now);

        for (var attempt = 1; attempt <= MaxSeqAttempts; attempt++)
        {
            tx.Seq = await users.NextSeqAsync(userId, ct);
            switch (await transactions.TryInsertAsync(userId, tx, ct))
            {
                case InsertOutcome.Inserted:
                    return new RecordResult(RecordOutcome.Recorded, tx);
                case InsertOutcome.DuplicateMessage:
                    // Nothing was stored: hand the number back so the next entry gets it (no gap).
                    await users.RewindSeqAsync(userId, expected: tx.Seq, value: tx.Seq - 1, ct);
                    return new RecordResult(RecordOutcome.Duplicate);
                case InsertOutcome.DuplicateSeq:
                    // Only after an /undo or /clear raced with this message: take the next number.
                    continue;
            }
        }
        throw new InvalidOperationException($"Could not allocate a free # for user {userId}.");
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

    /// <summary>
    /// Soft-deletes the user's most recent transaction and frees its #, so the next entry gets the same number.
    /// Only the latest entry can be removed, so no other transaction is ever renumbered.
    /// </summary>
    public async Task<Transaction?> UndoAsync(long userId, CancellationToken ct)
    {
        var current = await CurrentSeqAsync(userId, ct);
        var latest = await transactions.GetLatestAsync(userId, 2, ct);
        if (latest.Count == 0)
            return null;

        var deleted = await DeleteAsync(userId, latest[0], ct);
        if (deleted is not null)
        {
            // Continue right after the newest remaining entry. The rewind is skipped if a new
            // message took a number in the meantime.
            var remaining = latest.Count > 1 ? latest[1].Seq : 0;
            await users.RewindSeqAsync(userId, expected: current, value: remaining, ct);
        }
        return deleted;
    }

    /// <summary>Soft-deletes all of the user's transactions and restarts the numbering at #1.</summary>
    public async Task<long> ClearAsync(long userId, CancellationToken ct)
    {
        var current = await CurrentSeqAsync(userId, ct);
        var count = await transactions.SoftDeleteAllAsync(userId, Now, ct);
        await users.RewindSeqAsync(userId, expected: current, value: 0, ct);
        return count;
    }

    private async Task<long> CurrentSeqAsync(long userId, CancellationToken ct) =>
        (await users.GetAsync(userId, ct))?.NextSeq
        ?? throw new InvalidOperationException($"User {userId} does not exist.");

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
