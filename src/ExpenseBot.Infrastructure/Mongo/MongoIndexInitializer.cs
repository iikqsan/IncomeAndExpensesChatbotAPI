using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace ExpenseBot.Infrastructure.Mongo;

/// <summary>Creates every index from the design at startup (idempotent).</summary>
internal sealed class MongoIndexInitializer(MongoContext ctx, ILogger<MongoIndexInitializer> logger) : IHostedService
{
    /// <summary>
    /// Unique <c>{userId, seq}</c> over non-deleted transactions only, so a number freed by /undo or /clear
    /// can be given out again while the soft-deleted document keeps its old seq.
    /// </summary>
    public const string SeqIndexName = "userId_seq_active_unique";

    /// <summary>The earlier version of the seq index, which also covered deleted transactions.</summary>
    private const string LegacySeqIndexName = "userId_seq_unique";

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await ctx.Transactions.Indexes.DropOneAsync(LegacySeqIndexName, ct);
            logger.LogInformation("Dropped legacy index {Index}", LegacySeqIndexName);
        }
        catch (MongoCommandException ex) when (ex.CodeName is "IndexNotFound" or "NamespaceNotFound")
        {
            // Already migrated, or a fresh database.
        }

        var tx = Builders<TransactionDocument>.IndexKeys;
        await ctx.Transactions.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<TransactionDocument>(
                tx.Ascending(t => t.UserId).Descending(t => t.OccurredAt),
                new CreateIndexOptions { Name = "userId_occurredAt" }),
            new CreateIndexModel<TransactionDocument>(
                tx.Ascending(t => t.UserId).Ascending(t => t.MessageId),
                new CreateIndexOptions { Name = "userId_messageId_unique", Unique = true }),
            new CreateIndexModel<TransactionDocument>(
                tx.Ascending(t => t.UserId).Ascending(t => t.Seq),
                new CreateIndexOptions<TransactionDocument>
                {
                    Name = SeqIndexName,
                    Unique = true,
                    PartialFilterExpression = Builders<TransactionDocument>.Filter.Eq(t => t.IsDeleted, false),
                }),
        ], ct);

        var pu = Builders<ProcessedUpdateDocument>.IndexKeys;
        await ctx.ProcessedUpdates.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<ProcessedUpdateDocument>(
                pu.Ascending(p => p.UpdateId),
                new CreateIndexOptions { Name = "update_id_unique", Unique = true }),
            new CreateIndexModel<ProcessedUpdateDocument>(
                pu.Ascending(p => p.CreatedAt),
                new CreateIndexOptions { Name = "createdAt_ttl", ExpireAfter = TimeSpan.FromDays(1) }),
        ], ct);

        // users and categories only need the default _id index.
        logger.LogInformation("MongoDB indexes ensured on database {Database}", ctx.Database.DatabaseNamespace.DatabaseName);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
