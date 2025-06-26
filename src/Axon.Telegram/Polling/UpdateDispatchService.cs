using Axon.Telegram.Responders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Remora.Results;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Axon.Telegram.Polling;

/// <summary>
/// A service responsible for dispatching updates to registered responders.
/// </summary>
public sealed class UpdateDispatchService
{
    private readonly ILogger<UpdateDispatchService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateDispatchService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="scopeFactory">The service scope factory for creating scoped responders.</param>
    public UpdateDispatchService(ILogger<UpdateDispatchService> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Dispatches a given update to all relevant responders.
    /// </summary>
    /// <param name="update">The update to dispatch.</param>
    /// <param name="ct">A cancellation token for the operation.</param>
    /// <returns>A result that may or may not have succeeded.</returns>
    public async Task<Result> DispatchUpdateAsync(Update update, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;

        // Dispatch the entire update object
        await DispatchToRespondersAsync(services, update, ct);

        // Dispatch specific update types
        object? specificUpdate = update.Type switch
        {
            UpdateType.Message => update.Message,
            UpdateType.EditedMessage => update.EditedMessage,
            UpdateType.CallbackQuery => update.CallbackQuery,
            UpdateType.InlineQuery => update.InlineQuery,   
            UpdateType.ChosenInlineResult => update.ChosenInlineResult,
            UpdateType.MyChatMember => update.MyChatMember,
            UpdateType.ChatMember => update.ChatMember,
            UpdateType.ChatJoinRequest => update.ChatJoinRequest,
            _ => null
        };

        if (specificUpdate is null) return Result.FromSuccess();

        var dispatchMethod = GetType()
            .GetMethod(nameof(DispatchToRespondersAsync),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(specificUpdate.GetType());

        await (Task)dispatchMethod.Invoke(this, [services, specificUpdate, ct])!;

        return Result.FromSuccess();
    }

    private async Task DispatchToRespondersAsync<T>(IServiceProvider services, T update, CancellationToken ct)
    {
        var responders = services.GetServices<IResponder<T>>();
        foreach (var responder in responders)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var result = await responder.RespondAsync(update, ct);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning
                    (
                        "Responder {ResponderType} failed to respond to an event of type {EventType}: {Error}",
                        responder.GetType().Name,
                        typeof(T).Name,
                        result.Error.Message
                    );
                }
            }
            catch (Exception e)
            {
                _logger.LogError
                (
                    e,
                    "Responder {ResponderType} threw an unhandled exception while responding to an event of type {EventType}",
                    responder.GetType().Name,
                    typeof(T).Name
                );
            }
        }
    }
}