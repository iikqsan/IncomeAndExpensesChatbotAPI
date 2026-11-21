using ExpenseBot.Application.Abstractions;
using ExpenseBot.Application.Categories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ExpenseBot.Infrastructure.Mongo;

/// <summary>Seeds the default categories when the collection is empty, then warms the in-memory cache.</summary>
internal sealed class CategoryInitializer(
    ICategoryRepository repository,
    CategoryCache cache,
    ILogger<CategoryInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        if (await repository.SeedIfEmptyAsync(DefaultCategories.Seed(), ct))
            logger.LogInformation("Seeded default categories");

        await cache.ReloadAsync(ct);
        logger.LogInformation("Loaded {Count} categories into cache", cache.GetAll().Count);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
