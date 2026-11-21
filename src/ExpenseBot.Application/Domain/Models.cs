namespace ExpenseBot.Application.Domain;

public enum TransactionType
{
    Expense,
    Income,
}

public enum UserRole
{
    Member,
    Admin,
}

public enum UserStatus
{
    Pending,
    Active,
    Blocked,
}

public enum CategoryType
{
    Expense,
    Income,
    Both,
}

public sealed class Transaction
{
    public string? Id { get; set; }
    public long UserId { get; set; }
    public long Seq { get; set; }
    public long MessageId { get; set; }
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public string CategoryId { get; set; } = "";
    public string Description { get; set; } = "";
    public string RawText { get; set; } = "";

    /// <summary>Always UTC (from Telegram <c>message.date</c>).</summary>
    public DateTime OccurredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class BotUser
{
    /// <summary>Telegram user ID (<c>from.id</c>).</summary>
    public long Id { get; set; }
    public string? Username { get; set; }
    public string DisplayName { get; set; } = "";
    public UserRole Role { get; set; }
    public UserStatus Status { get; set; }
    public string Timezone { get; set; } = "Asia/Bangkok";
    public long NextSeq { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class Category
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public CategoryType Type { get; set; }
    public List<string> Keywords { get; set; } = [];
    public int SortOrder { get; set; }
}
