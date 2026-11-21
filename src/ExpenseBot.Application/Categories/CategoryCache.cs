using ExpenseBot.Application.Abstractions;
using ExpenseBot.Application.Domain;

namespace ExpenseBot.Application.Categories;

/// <summary>
/// In-memory cache of the household categories. Loaded at startup and reloaded whenever
/// an admin adds a keyword. Reads are lock-free snapshots.
/// </summary>
public sealed class CategoryCache(ICategoryRepository repository) : ICategoryProvider
{
    private static readonly Snapshot Fallback = new(
        DefaultCategories.Seed().Where(c => DefaultCategories.IsOther(c.Id)).ToList());

    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile Snapshot? _snapshot;

    public IReadOnlyList<Category> GetAll() => (_snapshot ?? Fallback).All;

    public Category? GetById(string id) => (_snapshot ?? Fallback).ById.GetValueOrDefault(id);

    public async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_snapshot is null)
            await ReloadAsync(ct);
    }

    /// <summary>Clears the cache and loads it again from the repository.</summary>
    public async Task ReloadAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var all = await repository.GetAllAsync(ct);
            _snapshot = new Snapshot(all.OrderBy(c => c.SortOrder).ToList());
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed class Snapshot(IReadOnlyList<Category> all)
    {
        public IReadOnlyList<Category> All { get; } = all;
        public Dictionary<string, Category> ById { get; } = all.ToDictionary(c => c.Id);
    }
}
