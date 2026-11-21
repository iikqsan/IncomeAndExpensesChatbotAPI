using ExpenseBot.Application.Domain;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ExpenseBot.Infrastructure.Mongo;

// Documents mirror the schema in docs/design.md. Enums are stored as lowercase strings,
// amounts as Decimal128 and dates as UTC.

internal sealed class UserDocument
{
    [BsonId] public long Id { get; set; }
    [BsonElement("username")] public string? Username { get; set; }
    [BsonElement("displayName")] public string DisplayName { get; set; } = "";
    [BsonElement("role")] public string Role { get; set; } = "member";
    [BsonElement("status")] public string Status { get; set; } = "pending";
    [BsonElement("timezone")] public string Timezone { get; set; } = "Asia/Bangkok";
    [BsonElement("nextSeq")] public long NextSeq { get; set; }
    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; }

    public BotUser ToDomain() => new()
    {
        Id = Id,
        Username = Username,
        DisplayName = DisplayName,
        Role = EnumText.Parse<UserRole>(Role),
        Status = EnumText.Parse<UserStatus>(Status),
        Timezone = Timezone,
        NextSeq = NextSeq,
        CreatedAt = CreatedAt,
    };
}

internal sealed class TransactionDocument
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("userId")] public long UserId { get; set; }
    [BsonElement("seq")] public long Seq { get; set; }
    [BsonElement("messageId")] public long MessageId { get; set; }
    [BsonElement("type")] public string Type { get; set; } = "expense";

    [BsonElement("amount"), BsonRepresentation(BsonType.Decimal128)]
    public decimal Amount { get; set; }

    [BsonElement("categoryId")] public string CategoryId { get; set; } = "";
    [BsonElement("description")] public string Description { get; set; } = "";
    [BsonElement("rawText")] public string RawText { get; set; } = "";
    [BsonElement("occurredAt")] public DateTime OccurredAt { get; set; }
    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; }
    [BsonElement("updatedAt")] public DateTime UpdatedAt { get; set; }
    [BsonElement("isDeleted")] public bool IsDeleted { get; set; }

    public static TransactionDocument FromDomain(Transaction t) => new()
    {
        Id = t.Id is null ? ObjectId.GenerateNewId() : ObjectId.Parse(t.Id),
        UserId = t.UserId,
        Seq = t.Seq,
        MessageId = t.MessageId,
        Type = EnumText.ToText(t.Type),
        Amount = t.Amount,
        CategoryId = t.CategoryId,
        Description = t.Description,
        RawText = t.RawText,
        OccurredAt = t.OccurredAt,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
        IsDeleted = t.IsDeleted,
    };

    public Transaction ToDomain() => new()
    {
        Id = Id.ToString(),
        UserId = UserId,
        Seq = Seq,
        MessageId = MessageId,
        Type = EnumText.Parse<TransactionType>(Type),
        Amount = Amount,
        CategoryId = CategoryId,
        Description = Description,
        RawText = RawText,
        OccurredAt = OccurredAt,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        IsDeleted = IsDeleted,
    };
}

internal sealed class ProcessedUpdateDocument
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("update_id")] public long UpdateId { get; set; }
    [BsonElement("userId")] public long UserId { get; set; }
    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; }
}

internal sealed class CategoryDocument
{
    [BsonId] public string Id { get; set; } = "";
    [BsonElement("name")] public string Name { get; set; } = "";
    [BsonElement("type")] public string Type { get; set; } = "expense";
    [BsonElement("keywords")] public List<string> Keywords { get; set; } = [];
    [BsonElement("sortOrder")] public int SortOrder { get; set; }

    public static CategoryDocument FromDomain(Category c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Type = EnumText.ToText(c.Type),
        Keywords = [.. c.Keywords],
        SortOrder = c.SortOrder,
    };

    public Category ToDomain() => new()
    {
        Id = Id,
        Name = Name,
        Type = EnumText.Parse<CategoryType>(Type),
        Keywords = [.. Keywords],
        SortOrder = SortOrder,
    };
}

internal static class EnumText
{
    public static string ToText<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    public static T Parse<T>(string text) where T : struct, Enum => Enum.Parse<T>(text, ignoreCase: true);
}
