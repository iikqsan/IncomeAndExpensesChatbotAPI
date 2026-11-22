using ExpenseBot.Application.Abstractions;
using ExpenseBot.Application.Categories;
using ExpenseBot.Application.Domain;
using ExpenseBot.Application.Formatting;
using ExpenseBot.Application.Options;
using ExpenseBot.Application.Summaries;
using ExpenseBot.Application.Transactions;
using ExpenseBot.Application.Updates;
using ExpenseBot.Application.Users;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExpenseBot.Application.Tests;

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FakeUsers : IUserRepository
{
    public Dictionary<long, BotUser> Store { get; } = [];

    public Task<BotUser> UpsertProfileAsync(long userId, string? username, string displayName, BotUser defaults, CancellationToken ct)
    {
        if (!Store.TryGetValue(userId, out var u))
            Store[userId] = u = new BotUser { Id = userId, Role = defaults.Role, Status = defaults.Status, Timezone = defaults.Timezone };
        u.Username = username;
        u.DisplayName = displayName;
        return Task.FromResult(Clone(u));
    }

    public Task<BotUser?> GetAsync(long userId, CancellationToken ct) =>
        Task.FromResult(Store.TryGetValue(userId, out var u) ? Clone(u) : null);

    public Task<long> NextSeqAsync(long userId, CancellationToken ct) => Task.FromResult(++Store[userId].NextSeq);

    public Task<bool> SetStatusAsync(long userId, UserStatus status, CancellationToken ct)
    {
        if (!Store.TryGetValue(userId, out var u)) return Task.FromResult(false);
        u.Status = status;
        return Task.FromResult(true);
    }

    public Task<BotUser> SetAccessAsync(long userId, UserRole role, UserStatus status, CancellationToken ct)
    {
        var u = Store[userId];
        u.Role = role;
        u.Status = status;
        return Task.FromResult(Clone(u));
    }

    public Task<IReadOnlyList<BotUser>> ListAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BotUser>>(Store.Values.Select(Clone).ToList());

    private static BotUser Clone(BotUser u) => new()
    {
        Id = u.Id, Username = u.Username, DisplayName = u.DisplayName, Role = u.Role, Status = u.Status,
        Timezone = u.Timezone, NextSeq = u.NextSeq, CreatedAt = u.CreatedAt,
    };
}

/// <summary>In-memory repository that applies the same userId / isDeleted filters as the Mongo one.</summary>
internal sealed class FakeTransactions : ITransactionRepository
{
    public List<Transaction> Items { get; } = [];

    private IEnumerable<Transaction> Active(long userId) => Items.Where(t => t.UserId == userId && !t.IsDeleted);

    public Task<bool> TryInsertAsync(long userId, Transaction t, CancellationToken ct)
    {
        if (Items.Any(x => x.UserId == userId && (x.MessageId == t.MessageId || x.Seq == t.Seq))) return Task.FromResult(false);
        t.Id = Guid.NewGuid().ToString("N");
        Items.Add(Copy(t));
        return Task.FromResult(true);
    }

    public Task<Transaction?> GetByMessageIdAsync(long userId, long messageId, CancellationToken ct) =>
        Task.FromResult(Active(userId).Where(t => t.MessageId == messageId).Select(Copy).FirstOrDefault());

    public Task<Transaction?> GetBySeqAsync(long userId, long seq, CancellationToken ct) =>
        Task.FromResult(Active(userId).Where(t => t.Seq == seq).Select(Copy).FirstOrDefault());

    public Task<IReadOnlyList<Transaction>> GetLatestAsync(long userId, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Transaction>>(Active(userId).OrderByDescending(t => t.Seq).Take(limit).Select(Copy).ToList());

    public Task<bool> UpdateParsedFieldsAsync(long userId, Transaction t, CancellationToken ct)
    {
        var stored = Active(userId).FirstOrDefault(x => x.Id == t.Id);
        if (stored is null) return Task.FromResult(false);
        (stored.Type, stored.Amount, stored.CategoryId, stored.Description, stored.RawText, stored.UpdatedAt) =
            (t.Type, t.Amount, t.CategoryId, t.Description, t.RawText, t.UpdatedAt);
        return Task.FromResult(true);
    }

    public Task<bool> SoftDeleteAsync(long userId, string transactionId, DateTime nowUtc, CancellationToken ct)
    {
        var stored = Active(userId).FirstOrDefault(x => x.Id == transactionId);
        if (stored is null) return Task.FromResult(false);
        stored.IsDeleted = true;
        stored.UpdatedAt = nowUtc;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<Transaction>> GetByCategoryAsync(long userId, string categoryId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Transaction>>(Active(userId)
            .Where(t => t.CategoryId == categoryId && t.OccurredAt >= fromUtc && t.OccurredAt < toUtc)
            .OrderBy(t => t.OccurredAt)
            .Select(Copy)
            .ToList());

    public Task<IReadOnlyList<CategoryTotal>> SummarizeAsync(long userId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CategoryTotal>>(Active(userId)
            .Where(t => t.OccurredAt >= fromUtc && t.OccurredAt < toUtc)
            .GroupBy(t => (t.Type, t.CategoryId))
            .Select(g => new CategoryTotal(g.Key.Type, g.Key.CategoryId, g.Sum(t => t.Amount), g.Count()))
            .ToList());

    private static Transaction Copy(Transaction t) => new()
    {
        Id = t.Id, UserId = t.UserId, Seq = t.Seq, MessageId = t.MessageId, Type = t.Type, Amount = t.Amount,
        CategoryId = t.CategoryId, Description = t.Description, RawText = t.RawText, OccurredAt = t.OccurredAt,
        CreatedAt = t.CreatedAt, UpdatedAt = t.UpdatedAt, IsDeleted = t.IsDeleted,
    };
}

internal sealed class FakeProcessed : IProcessedUpdateRepository
{
    private readonly HashSet<long> _seen = [];
    public Task<bool> TryMarkProcessedAsync(long userId, long updateId, CancellationToken ct) => Task.FromResult(_seen.Add(updateId));
}

internal sealed class FakeCategoryRepository : ICategoryRepository
{
    public List<Category> Items { get; } = [];
    public int Loads { get; private set; }

    public Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct)
    {
        Loads++;
        return Task.FromResult<IReadOnlyList<Category>>(Items
            .Select(c => new Category { Id = c.Id, Name = c.Name, Type = c.Type, SortOrder = c.SortOrder, Keywords = [.. c.Keywords] })
            .ToList());
    }

    public Task<bool> SeedIfEmptyAsync(IEnumerable<Category> categories, CancellationToken ct)
    {
        if (Items.Count > 0) return Task.FromResult(false);
        Items.AddRange(categories);
        return Task.FromResult(true);
    }

    public Task<bool> AddKeywordAsync(string categoryId, string keyword, CancellationToken ct)
    {
        var c = Items.First(x => x.Id == categoryId);
        if (c.Keywords.Contains(keyword)) return Task.FromResult(false);
        c.Keywords.Add(keyword);
        return Task.FromResult(true);
    }
}

internal sealed class FakeMessenger : IBotMessenger
{
    public List<(long ChatId, string Html, IReadOnlyList<InlineButton>? Buttons)> Sent { get; } = [];
    public List<(long ChatId, long MessageId, string Html)> Edits { get; } = [];
    public List<(string Id, string? Text)> CallbackAnswers { get; } = [];
    public List<long> LeftChats { get; } = [];

    public Task SendHtmlAsync(long chatId, string html, CancellationToken ct, IReadOnlyList<InlineButton>? buttons = null)
    {
        Sent.Add((chatId, html, buttons));
        return Task.CompletedTask;
    }

    public Task EditHtmlAsync(long chatId, long messageId, string html, CancellationToken ct)
    {
        Edits.Add((chatId, messageId, html));
        return Task.CompletedTask;
    }

    public Task AnswerCallbackAsync(string callbackQueryId, string? text, CancellationToken ct)
    {
        CallbackAnswers.Add((callbackQueryId, text));
        return Task.CompletedTask;
    }

    public Task LeaveChatAsync(long chatId, CancellationToken ct)
    {
        LeftChats.Add(chatId);
        return Task.CompletedTask;
    }

    public IEnumerable<string> To(long chatId) => Sent.Where(s => s.ChatId == chatId).Select(s => s.Html);
}

/// <summary>Wires the real Application services on top of the in-memory fakes.</summary>
internal sealed class TestApp
{
    public const long Admin = 1001;

    public FakeUsers Users { get; } = new();
    public FakeTransactions Transactions { get; } = new();
    public FakeCategoryRepository CategoryRepo { get; } = new();
    public FakeMessenger Messenger { get; } = new();
    public FixedClock Clock { get; } = new(new DateTimeOffset(2026, 11, 21, 5, 0, 0, TimeSpan.Zero)); // 12:00 Bangkok
    public CategoryCache Cache { get; }
    public TransactionFormatter Formatter { get; }
    public UpdateHandler Handler { get; }

    private long _nextUpdateId = 1;

    public TestApp()
    {
        CategoryRepo.Items.AddRange(DefaultCategories.Seed());
        Cache = new CategoryCache(CategoryRepo);
        Formatter = new TransactionFormatter(Cache);

        var options = Microsoft.Extensions.Options.Options.Create(new BotOptions { AdminUserId = Admin });
        var userService = new UserService(Users, options);
        var txService = new TransactionService(Transactions, Users, Cache, Clock);
        var summary = new SummaryService(Transactions, Clock, NullLogger<SummaryService>.Instance);
        var commands = new CommandHandler(txService, summary, userService, CategoryRepo, Cache, Messenger, Formatter);

        Handler = new UpdateHandler(userService, txService, commands, Cache, new FakeProcessed(), Messenger, Formatter,
            NullLogger<UpdateHandler>.Instance);
    }

    public static BotSender Sender(long id) => new(id, $"user{id}", $"คุณ{id}", null);

    public Task Send(long from, string text, long? messageId = null, DateTime? dateUtc = null, ChatKind chat = ChatKind.Private)
    {
        var id = _nextUpdateId++;
        return Handler.HandleAsync(new BotUpdate
        {
            UpdateId = id,
            Kind = BotUpdateKind.Message,
            ChatId = chat == ChatKind.Private ? from : -5000,
            Chat = chat,
            From = Sender(from),
            MessageId = messageId ?? id,
            Text = text,
            DateUtc = dateUtc ?? Clock.Now.UtcDateTime,
        }, default);
    }

    public Task Edit(long from, long messageId, string text) =>
        Handler.HandleAsync(new BotUpdate
        {
            UpdateId = _nextUpdateId++,
            Kind = BotUpdateKind.EditedMessage,
            ChatId = from,
            Chat = ChatKind.Private,
            From = Sender(from),
            MessageId = messageId,
            Text = text,
            DateUtc = Clock.Now.UtcDateTime,
        }, default);

    public Task Callback(long from, string data, long messageId = 777) =>
        Handler.HandleAsync(new BotUpdate
        {
            UpdateId = _nextUpdateId++,
            Kind = BotUpdateKind.CallbackQuery,
            ChatId = from,
            Chat = ChatKind.Private,
            From = Sender(from),
            MessageId = messageId,
            Text = data,
            CallbackQueryId = "cb" + _nextUpdateId,
        }, default);

    /// <summary>Registers and approves a member.</summary>
    public async Task AddActiveMember(long userId)
    {
        await Send(userId, "/start");
        await Callback(Admin, UpdateHandler.ApprovePrefix + userId);
    }

    public string LastTo(long chatId) => Messenger.To(chatId).Last();
}
