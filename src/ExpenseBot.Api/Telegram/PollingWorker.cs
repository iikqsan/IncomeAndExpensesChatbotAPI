using ExpenseBot.Application.Updates;
using ExpenseBot.Infrastructure.Telegram;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace ExpenseBot.Api.Telegram;

/// <summary>
/// Long polling loop. Only runs when <c>Telegram:Mode = Polling</c>; it first deletes any registered
/// webhook, because getUpdates does not work while one is set.
/// </summary>
public sealed class PollingWorker(
    ITelegramBotClient bot,
    IServiceScopeFactory scopes,
    IOptions<TelegramOptions> options,
    ILogger<PollingWorker> logger) : BackgroundService
{
    private static readonly UpdateType[] AllowedUpdates = TelegramUpdateTypes.Allowed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Mode != TelegramMode.Polling)
            return;

        int? offset = null;
        var webhookCleared = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!webhookCleared)
                {
                    // getUpdates does not work while a webhook is registered.
                    await bot.DeleteWebhook(dropPendingUpdates: false, cancellationToken: stoppingToken);
                    webhookCleared = true;
                    logger.LogInformation("Telegram long polling started");
                }

                var updates = await bot.GetUpdates(offset, timeout: 30, allowedUpdates: AllowedUpdates,
                    cancellationToken: stoppingToken);

                foreach (var update in updates)
                {
                    offset = update.Id + 1;
                    await using var scope = scopes.CreateAsyncScope();
                    var handler = scope.ServiceProvider.GetRequiredService<UpdateHandler>();
                    try
                    {
                        await handler.HandleAsync(TelegramUpdateMapper.Map(update), stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "Failed to handle update {UpdateId}", update.Id);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Polling error, retrying in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
