using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace ExpenseBot.Infrastructure.Mongo;

public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    public string ConnectionString { get; set; } = "";
    public string Database { get; set; } = "expensebot";
}

internal sealed class MongoContext
{
    public MongoContext(IOptions<MongoOptions> options)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.ConnectionString))
            throw new InvalidOperationException("Mongo:ConnectionString is not configured.");

        var client = new MongoClient(o.ConnectionString);
        Database = client.GetDatabase(o.Database);
    }

    public IMongoDatabase Database { get; }

    public IMongoCollection<UserDocument> Users => Database.GetCollection<UserDocument>("users");
    public IMongoCollection<TransactionDocument> Transactions => Database.GetCollection<TransactionDocument>("transactions");
    public IMongoCollection<CategoryDocument> Categories => Database.GetCollection<CategoryDocument>("categories");
    public IMongoCollection<ProcessedUpdateDocument> ProcessedUpdates => Database.GetCollection<ProcessedUpdateDocument>("processed_updates");
}
