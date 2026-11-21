using ExpenseBot.Application.Abstractions;
using ExpenseBot.Application.Domain;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ExpenseBot.Infrastructure.Mongo;

internal sealed class MongoUserRepository(MongoContext ctx, TimeProvider clock) : IUserRepository
{
    private static FilterDefinition<UserDocument> ById(long userId) => Builders<UserDocument>.Filter.Eq(u => u.Id, userId);

    public async Task<BotUser> UpsertProfileAsync(
        long userId, string? username, string displayName, BotUser defaults, CancellationToken ct)
    {
        var update = Builders<UserDocument>.Update
            .Set(u => u.Username, username)
            .Set(u => u.DisplayName, displayName)
            .SetOnInsert(u => u.Role, EnumText.ToText(defaults.Role))
            .SetOnInsert(u => u.Status, EnumText.ToText(defaults.Status))
            .SetOnInsert(u => u.Timezone, defaults.Timezone)
            .SetOnInsert(u => u.NextSeq, 0L)
            .SetOnInsert(u => u.CreatedAt, clock.GetUtcNow().UtcDateTime);

        var doc = await ctx.Users.FindOneAndUpdateAsync(ById(userId), update,
            new FindOneAndUpdateOptions<UserDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After }, ct);

        return doc.ToDomain();
    }

    public async Task<BotUser?> GetAsync(long userId, CancellationToken ct)
    {
        var doc = await ctx.Users.Find(ById(userId)).FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }

    public async Task<long> NextSeqAsync(long userId, CancellationToken ct)
    {
        var doc = await ctx.Users.FindOneAndUpdateAsync(ById(userId),
            Builders<UserDocument>.Update.Inc(u => u.NextSeq, 1L),
            new FindOneAndUpdateOptions<UserDocument> { ReturnDocument = ReturnDocument.After }, ct);

        return doc?.NextSeq ?? throw new InvalidOperationException($"User {userId} does not exist.");
    }

    public async Task<bool> SetStatusAsync(long userId, UserStatus status, CancellationToken ct)
    {
        var result = await ctx.Users.UpdateOneAsync(ById(userId),
            Builders<UserDocument>.Update.Set(u => u.Status, EnumText.ToText(status)), cancellationToken: ct);
        return result.MatchedCount == 1;
    }

    public async Task<BotUser> SetAccessAsync(long userId, UserRole role, UserStatus status, CancellationToken ct)
    {
        var doc = await ctx.Users.FindOneAndUpdateAsync(ById(userId),
            Builders<UserDocument>.Update
                .Set(u => u.Role, EnumText.ToText(role))
                .Set(u => u.Status, EnumText.ToText(status)),
            new FindOneAndUpdateOptions<UserDocument> { ReturnDocument = ReturnDocument.After }, ct);

        return doc?.ToDomain() ?? throw new InvalidOperationException($"User {userId} does not exist.");
    }

    public async Task<IReadOnlyList<BotUser>> ListAllAsync(CancellationToken ct)
    {
        var docs = await ctx.Users.Find(FilterDefinition<UserDocument>.Empty)
            .SortBy(u => u.CreatedAt)
            .ToListAsync(ct);
        return docs.Select(d => d.ToDomain()).ToList();
    }
}

internal sealed class MongoTransactionRepository(MongoContext ctx) : ITransactionRepository
{
    private static readonly FilterDefinitionBuilder<TransactionDocument> F = Builders<TransactionDocument>.Filter;

    /// <summary>Base filter for reads: always the caller's own, never deleted.</summary>
    private static FilterDefinition<TransactionDocument> Active(long userId) =>
        F.Eq(t => t.UserId, userId) & F.Eq(t => t.IsDeleted, false);

    public async Task<bool> TryInsertAsync(long userId, Transaction transaction, CancellationToken ct)
    {
        if (transaction.UserId != userId)
            throw new ArgumentException("Transaction does not belong to the given user.", nameof(transaction));

        var doc = TransactionDocument.FromDomain(transaction);
        try
        {
            await ctx.Transactions.InsertOneAsync(doc, cancellationToken: ct);
            transaction.Id = doc.Id.ToString();
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<Transaction?> GetByMessageIdAsync(long userId, long messageId, CancellationToken ct)
    {
        var doc = await ctx.Transactions.Find(Active(userId) & F.Eq(t => t.MessageId, messageId)).FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }

    public async Task<Transaction?> GetBySeqAsync(long userId, long seq, CancellationToken ct)
    {
        var doc = await ctx.Transactions.Find(Active(userId) & F.Eq(t => t.Seq, seq)).FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }

    public async Task<IReadOnlyList<Transaction>> GetLatestAsync(long userId, int limit, CancellationToken ct)
    {
        var docs = await ctx.Transactions.Find(Active(userId))
            .SortByDescending(t => t.Seq)
            .Limit(limit)
            .ToListAsync(ct);
        return docs.Select(d => d.ToDomain()).ToList();
    }

    public async Task<bool> UpdateParsedFieldsAsync(long userId, Transaction t, CancellationToken ct)
    {
        var update = Builders<TransactionDocument>.Update
            .Set(d => d.Type, EnumText.ToText(t.Type))
            .Set(d => d.Amount, t.Amount)
            .Set(d => d.CategoryId, t.CategoryId)
            .Set(d => d.Description, t.Description)
            .Set(d => d.RawText, t.RawText)
            .Set(d => d.UpdatedAt, t.UpdatedAt);

        var result = await ctx.Transactions.UpdateOneAsync(
            Active(userId) & F.Eq(d => d.Id, ObjectId.Parse(t.Id)), update, cancellationToken: ct);
        return result.MatchedCount == 1;
    }

    public async Task<bool> SoftDeleteAsync(long userId, string transactionId, DateTime nowUtc, CancellationToken ct)
    {
        var result = await ctx.Transactions.UpdateOneAsync(
            Active(userId) & F.Eq(d => d.Id, ObjectId.Parse(transactionId)),
            Builders<TransactionDocument>.Update.Set(d => d.IsDeleted, true).Set(d => d.UpdatedAt, nowUtc),
            cancellationToken: ct);
        return result.ModifiedCount == 1;
    }

    public async Task<IReadOnlyList<CategoryTotal>> SummarizeAsync(long userId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        // $match (own, not deleted, period) → $group by type + category → $sum
        BsonDocument[] pipeline =
        [
            new("$match", new BsonDocument
            {
                { "userId", userId },
                { "isDeleted", false },
                { "occurredAt", new BsonDocument { { "$gte", fromUtc }, { "$lt", toUtc } } },
            }),
            new("$group", new BsonDocument
            {
                { "_id", new BsonDocument { { "type", "$type" }, { "categoryId", "$categoryId" } } },
                { "total", new BsonDocument("$sum", "$amount") },
                { "count", new BsonDocument("$sum", 1) },
            }),
        ];

        var rows = await ctx.Transactions.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).ToListAsync(ct);
        return rows.Select(r => new CategoryTotal(
                EnumText.Parse<TransactionType>(r["_id"]["type"].AsString),
                r["_id"]["categoryId"].AsString,
                r["total"].ToDecimal(),
                r["count"].ToInt32()))
            .ToList();
    }
}

internal sealed class MongoProcessedUpdateRepository(MongoContext ctx, TimeProvider clock) : IProcessedUpdateRepository
{
    public async Task<bool> TryMarkProcessedAsync(long userId, long updateId, CancellationToken ct)
    {
        try
        {
            await ctx.ProcessedUpdates.InsertOneAsync(new ProcessedUpdateDocument
            {
                UpdateId = updateId,
                UserId = userId,
                CreatedAt = clock.GetUtcNow().UtcDateTime,
            }, cancellationToken: ct);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }
}

internal sealed class MongoCategoryRepository(MongoContext ctx) : ICategoryRepository
{
    public async Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct)
    {
        var docs = await ctx.Categories.Find(FilterDefinition<CategoryDocument>.Empty).SortBy(c => c.SortOrder).ToListAsync(ct);
        return docs.Select(d => d.ToDomain()).ToList();
    }

    public async Task<bool> SeedIfEmptyAsync(IEnumerable<Category> categories, CancellationToken ct)
    {
        if (await ctx.Categories.EstimatedDocumentCountAsync(cancellationToken: ct) > 0)
            return false;

        try
        {
            await ctx.Categories.InsertManyAsync(categories.Select(CategoryDocument.FromDomain),
                new InsertManyOptions { IsOrdered = false }, ct);
        }
        catch (MongoBulkWriteException ex) when (ex.WriteErrors.All(e => e.Category == ServerErrorCategory.DuplicateKey))
        {
            // Another instance seeded concurrently.
        }
        return true;
    }

    public async Task<bool> AddKeywordAsync(string categoryId, string keyword, CancellationToken ct)
    {
        var result = await ctx.Categories.UpdateOneAsync(
            Builders<CategoryDocument>.Filter.Eq(c => c.Id, categoryId),
            Builders<CategoryDocument>.Update.AddToSet(c => c.Keywords, keyword),
            cancellationToken: ct);
        return result.ModifiedCount == 1;
    }
}
