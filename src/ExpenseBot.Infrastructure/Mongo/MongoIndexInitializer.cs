using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace ExpenseBot.Infrastructure.Mongo;

/// <summary>Creates every index from the design at startup (idempotent).</summary>
internal sealed class MongoIndexInitializer(MongoContext ctx, ILogger<MongoIndexInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
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
                new CreateIndexOptions { Name = "userId_seq_unique", Unique = true }),
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
