using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Remora.Results;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace Axon.Telegram.Polling;

/// <summary>
/// A client that handles long-polling for updates from the Telegram API.
/// This is the logical equivalent of Remora.Discord's Gateway Client.
/// </summary>
public sealed class TelegramPollingClient
{
    private readonly ILogger<TelegramPollingClient> _logger;
    private readonly ITelegramBotClient _botClient;
    private readonly UpdateDispatchService _dispatchService;
    private readonly TelegramPollingClientOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="TelegramPollingClient"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="botClient">The telegram bot client.</param>
    /// <param name="dispatchService">The service for dispatching updates to responders.</param>
    /// <param name="options">The polling client options.</param>
    public TelegramPollingClient
    (
        ILogger<TelegramPollingClient> logger,
        ITelegramBotClient botClient,
        UpdateDispatchService dispatchService,
        IOptions<TelegramPollingClientOptions> options
    )
    {
        _logger = logger;
        _botClient = botClient;
        _dispatchService = dispatchService;
        _options = options.Value;
    }

    /// <summary>
    /// Runs the polling client, continuously fetching and dispatching updates.
    /// </summary>
    /// <param name="ct">A cancellation token to stop the client.</param>
    /// <returns>A result indicating the success or failure of the operation.</returns>
    public async Task<Result> RunAsync(CancellationToken ct = default)
    {
        var me = await _botClient.GetMe(ct);
        _logger.LogInformation("Successfully connected to Telegram. Hello, I am {BotName}!", me.FirstName);

        try
        {
            await _botClient.ReceiveAsync
            (
                updateHandler: HandleUpdateAsync,
                errorHandler: HandlePollingErrorAsync,
                receiverOptions: _options.ReceiverOptions ?? new ReceiverOptions { DropPendingUpdates = true },
                cancellationToken: ct
            );

            return Result.FromSuccess();
        }
        catch (Exception e)
        {
            return Result.FromError(new ExceptionError(e, "The polling client encountered an unhandled exception."));
        }
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        try
        {
            var result = await _dispatchService.DispatchUpdateAsync(update, ct);
            if (!result.IsSuccess)
            {
                _logger.LogWarning("An error occurred while dispatching an update: {Error}", result.Error.Message);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "An unhandled exception occurred during update dispatch.");
        }
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken ct)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException =>
                $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        _logger.LogError("Polling Error: {ErrorMessage}", errorMessage);
        return Task.CompletedTask;
    }
}