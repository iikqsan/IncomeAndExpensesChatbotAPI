using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ExpenseBot.Application.Updates;
using ExpenseBot.Infrastructure.Telegram;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ExpenseBot.Api.Telegram;

/// <summary>
/// Buffer between the webhook endpoint and <see cref="UpdateHandler"/>, so the endpoint can return 200
/// to Telegram immediately. Updates are processed one at a time, in arrival order.
/// </summary>
public sealed class UpdateQueue
{
    public const int Capacity = 1000;

    private readonly Channel<Update> _channel = Channel.CreateBounded<Update>(
        new BoundedChannelOptions(Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    public bool TryEnqueue(Update update) => _channel.Writer.TryWrite(update);

    public IAsyncEnumerable<Update> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

public static class WebhookEndpoint
{
    public const string Path = "/telegram/webhook";
    public const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    public static void MapTelegramWebhook(this WebApplication app)
    {
        app.MapPost(Path, HandleAsync).ExcludeFromDescription();
    }

    private static async Task<IResult> HandleAsync(
        HttpRequest request,
        UpdateQueue queue,
        IOptions<TelegramOptions> options,
        ILogger<UpdateQueue> logger,
        CancellationToken ct)
    {
        // 1. Reject anything that does not carry our secret (checked before reading the body).
        if (!SecretMatches(request.Headers[SecretHeader], options.Value.WebhookSecret))
        {
            logger.LogWarning("Webhook request with missing or invalid secret token from {Ip}",
                request.HttpContext.Connection.RemoteIpAddress);
            return Results.Unauthorized();
        }

        Update? update;
        try
        {
            update = await JsonSerializer.DeserializeAsync<Update>(request.Body, JsonBotAPI.Options, ct);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Webhook body is not a valid Telegram update");
            return Results.BadRequest();
        }

        if (update is null)
            return Results.BadRequest();

        // 2. Hand off and answer right away. If the queue is full, 503 makes Telegram retry later.
        if (!queue.TryEnqueue(update))
        {
            logger.LogError("Update queue is full; rejecting update {UpdateId}", update.Id);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok();
    }

    internal static bool SecretMatches(string? received, string? expected)
    {
        if (string.IsNullOrEmpty(received) || string.IsNullOrEmpty(expected))
            return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(received), Encoding.UTF8.GetBytes(expected));
    }
}

/// <summary>Drains <see cref="UpdateQueue"/> into the same <see cref="UpdateHandler"/> the polling worker uses.</summary>
public sealed class WebhookProcessor(UpdateQueue queue, IServiceScopeFactory scopes, ILogger<WebhookProcessor> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var update in queue.ReadAllAsync(stoppingToken))
        {
            await using var scope = scopes.CreateAsyncScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<UpdateHandler>()
                    .HandleAsync(TelegramUpdateMapper.Map(update), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to handle update {UpdateId}", update.Id);
            }
        }
    }
}

/// <summary>Registers the webhook (URL + secret token) at startup, retrying until Telegram accepts it.</summary>
public sealed class WebhookRegistrar(ITelegramBotClient bot, IOptions<TelegramOptions> options, ILogger<WebhookRegistrar> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        var delay = TimeSpan.FromSeconds(5);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await bot.SetWebhook(o.WebhookUrl!, allowedUpdates: TelegramUpdateTypes.Allowed,
                    secretToken: o.WebhookSecret, dropPendingUpdates: false, cancellationToken: stoppingToken);
                logger.LogInformation("Telegram webhook registered at {Url}", o.WebhookUrl);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not register webhook, retrying in {Delay}", delay);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 300));
            }
        }
    }
}
