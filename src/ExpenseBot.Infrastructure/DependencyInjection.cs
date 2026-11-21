using ExpenseBot.Application.Abstractions;
using ExpenseBot.Infrastructure.Mongo;
using ExpenseBot.Infrastructure.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace ExpenseBot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<MongoOptions>(config.GetSection(MongoOptions.SectionName));
        services.Configure<TelegramOptions>(config.GetSection(TelegramOptions.SectionName));

        services.AddSingleton<MongoContext>();
        services.AddSingleton<IUserRepository, MongoUserRepository>();
        services.AddSingleton<ITransactionRepository, MongoTransactionRepository>();
        services.AddSingleton<IProcessedUpdateRepository, MongoProcessedUpdateRepository>();
        services.AddSingleton<ICategoryRepository, MongoCategoryRepository>();
        // Order matters: indexes first, then category seed + cache warm-up (before polling starts).
        services.AddHostedService<MongoIndexInitializer>();
        services.AddHostedService<CategoryInitializer>();

        services.AddHttpClient("telegram");
        services.AddSingleton<ITelegramBotClient>(sp =>
        {
            var token = sp.GetRequiredService<IOptions<TelegramOptions>>().Value.BotToken;
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Telegram:BotToken is not configured.");
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("telegram");
            return new TelegramBotClient(token, http);
        });
        services.AddSingleton<IBotMessenger, TelegramBotMessenger>();

        return services;
    }
}
