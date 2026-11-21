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
        services.AddOptions<TelegramOptions>()
            .Bind(config.GetSection(TelegramOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.BotToken), "Telegram:BotToken is required.")
            .Validate(o => o.Mode != TelegramMode.Webhook || IsHttpsUrl(o.WebhookUrl),
                "Telegram:WebhookUrl must be an absolute https:// URL when Telegram:Mode = Webhook.")
            .Validate(o => o.Mode != TelegramMode.Webhook || IsValidSecret(o.WebhookSecret),
                "Telegram:WebhookSecret is required in Webhook mode: 1-256 characters of A-Z, a-z, 0-9, _ or -.")
            .ValidateOnStart();

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

    private static bool IsHttpsUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    // Telegram's rule for secret_token.
    private static bool IsValidSecret(string? secret) =>
        secret is { Length: >= 1 and <= 256 } && secret.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}
