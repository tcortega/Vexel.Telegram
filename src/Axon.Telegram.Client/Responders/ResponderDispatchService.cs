using System.Threading.Channels;
using Axon.Telegram.Abstractions.Responders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Remora.Results;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Axon.Telegram.Client.Responders;

/// <inheritdoc cref="IResponderDispatchService"/>
public class ResponderDispatchService : IAsyncDisposable, IResponderDispatchService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ResponderDispatchService> _logger;
    private readonly IResponderTypeRepository _responderTypeRepository;

    private readonly Task _dispatcher;
    private readonly Task _finalizer;
    private readonly Channel<Update> _updatesToDispatch;
    private readonly Channel<Task<IReadOnlyList<Result>>> _respondersToFinalize;

    /// <summary>
    /// Holds the token source used to get tokens for running responders. Execution of the dispatch service's own tasks
    /// is controlled via the channels.
    /// </summary>
    private readonly CancellationTokenSource _responderCancellationSource;

    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResponderDispatchService"/> class.
    /// </summary>
    /// <param name="services">The available services.</param>
    /// <param name="responderTypeRepository">The responder type repository.</param>
    /// <param name="logger">The logging instance for this type.</param>
    /// <param name="options">Options for dispatch.</param>
    public ResponderDispatchService
    (
        IServiceProvider services,
        IResponderTypeRepository responderTypeRepository,
        ILogger<ResponderDispatchService> logger,
        IOptions<ResponderDispatchOptions> options
    )
    {
        _services = services;
        _responderTypeRepository = responderTypeRepository;
        _logger = logger;

        _responderCancellationSource = new();
        _updatesToDispatch = Channel.CreateBounded<Update>
        (
            new BoundedChannelOptions((int)options.Value.MaxItems)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true
            }
        );

        _respondersToFinalize = Channel.CreateUnbounded<Task<IReadOnlyList<Result>>>
        (
            new()
            {
                SingleReader = true
            }
        );

        _dispatcher = Task.Run(DispatcherTaskAsync, CancellationToken.None);
        _finalizer = Task.Run(FinalizerTaskAsync, CancellationToken.None);
    }

    /// <summary>
    /// Dispatches the given update to interested responders. If the service is stopped with pending dispatches, the
    /// pending updates will be dropped. Any updates that have been dispatched (that is, a call to this method has
    /// returned a successful result) will be allowed to run to completion.
    /// </summary>
    /// <param name="update">The update to dispatch.</param>
    /// <param name="ct">
    /// The cancellation token for this operation. Note that this is *not* the cancellation token that will be passed
    /// to any instantiated responders.
    /// </param>
    /// <returns>A result which may or may not have succeeded.</returns>
    public async Task<Result> DispatchAsync(Update update, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, nameof(ResponderDispatchService));

        try
        {
            await _updatesToDispatch.Writer.WriteAsync(update, ct);
        }
        catch (Exception e)
        {
            return e;
        }

        return Result.FromSuccess();
    }

    /// <summary>
    /// Runs the main loop of the dispatcher task.
    /// </summary>
    private async Task DispatcherTaskAsync()
    {
        try
        {
            while (await _updatesToDispatch.Reader.WaitToReadAsync())
            {
                var update = await _updatesToDispatch.Reader.ReadAsync();
                var dispatch = UnwrapAndDispatchEvent(update);
                if (!dispatch.IsSuccess)
                {
                    _logger.LogWarning("Failed to dispatch update: {Reason}", dispatch.Error.Message);
                    continue;
                }

                await _respondersToFinalize.Writer.WriteAsync(dispatch.Entity);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ChannelClosedException)
        {
            // this is fine, no further incoming updates to accept
        }

        _respondersToFinalize.Writer.Complete();
    }

    /// <summary>
    /// Runs the main loop of the finalizer task.
    /// </summary>
    private async Task FinalizerTaskAsync()
    {
        if (_responderCancellationSource is null || _respondersToFinalize is null)
        {
            throw new InvalidOperationException();
        }

        try
        {
            while (await _respondersToFinalize.Reader.WaitToReadAsync())
            {
                var responderResults = await _respondersToFinalize.Reader.ReadAsync();
                if (!responderResults.IsCompleted)
                {
                    var timeout = Task.Delay(TimeSpan.FromMilliseconds(10));

                    var finishedTask = await Task.WhenAny(responderResults, timeout);
                    if (finishedTask == timeout)
                    {
                        // This responder is taking too long... put it back on the channel and look at some other stuff
                        // in the meantime.
                        try
                        {
                            await _respondersToFinalize.Writer.WriteAsync(responderResults);
                            continue;
                        }
                        catch (ChannelClosedException)
                        {
                            // Okay, we can't put it back on, so we'll just drop out and await it. It should be the last
                            // item in the pipe anyway
                        }
                    }
                }

                FinalizeResponderDispatch(await responderResults);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ChannelClosedException)
        {
            // this is fine, nothing further to do
        }
    }

    /// <summary>
    /// Unwraps the given update into its typed representation, dispatching all events for it.
    /// </summary>
    /// <param name="update">The update.</param>
    private Result<Task<IReadOnlyList<Result>>> UnwrapAndDispatchEvent(Update update)
    {
        try
        {
            var responderTask = update.Type switch
            {
                UpdateType.Message => DispatchUpdateAsync(update.Message!),
                UpdateType.EditedMessage => DispatchUpdateAsync(update.EditedMessage!),
                UpdateType.CallbackQuery => DispatchUpdateAsync(update.CallbackQuery!),
                UpdateType.InlineQuery => DispatchUpdateAsync(update.InlineQuery!),
                UpdateType.ChosenInlineResult => DispatchUpdateAsync(update.ChosenInlineResult!),
                UpdateType.ChannelPost => DispatchUpdateAsync(update.ChannelPost!),
                UpdateType.EditedChannelPost => DispatchUpdateAsync(update.EditedChannelPost!),
                UpdateType.ShippingQuery => DispatchUpdateAsync(update.ShippingQuery!),
                UpdateType.PreCheckoutQuery => DispatchUpdateAsync(update.PreCheckoutQuery!),
                UpdateType.Poll => DispatchUpdateAsync(update.Poll!),
                UpdateType.PollAnswer => DispatchUpdateAsync(update.PollAnswer!),
                UpdateType.MyChatMember => DispatchUpdateAsync(update.MyChatMember!),
                UpdateType.ChatMember => DispatchUpdateAsync(update.ChatMember!),
                UpdateType.ChatJoinRequest => DispatchUpdateAsync(update.ChatJoinRequest!),
                UpdateType.MessageReaction => DispatchUpdateAsync(update.MessageReaction!),
                UpdateType.MessageReactionCount => DispatchUpdateAsync(update.MessageReactionCount!),
                UpdateType.ChatBoost => DispatchUpdateAsync(update.ChatBoost!),
                UpdateType.RemovedChatBoost => DispatchUpdateAsync(update.RemovedChatBoost!),
                UpdateType.BusinessConnection => DispatchUpdateAsync(update.BusinessConnection!),
                UpdateType.BusinessMessage => DispatchUpdateAsync(update.BusinessMessage!),
                UpdateType.EditedBusinessMessage => DispatchUpdateAsync(update.EditedBusinessMessage!),
                UpdateType.DeletedBusinessMessages => DispatchUpdateAsync(update.DeletedBusinessMessages!),
                UpdateType.PurchasedPaidMedia => DispatchUpdateAsync(update.PurchasedPaidMedia!),
                _ => LogAndReturnUnhandled(update.Type)
            };

            return responderTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "An unhandled exception occurred during background processing of update {UpdateId} of type {UpdateType}",
                update.Id, update.Type);

            throw;
        }
    }

    private async Task<IReadOnlyList<Result>> DispatchUpdateAsync<TUpdate>(TUpdate update)
    {
        var responderGroups = new[]
        {
            _responderTypeRepository.GetEarlyResponderTypes<TUpdate>(),
            _responderTypeRepository.GetResponderTypes<TUpdate>(),
            _responderTypeRepository.GetLateResponderTypes<TUpdate>()
        };

        var results = new List<Result>();
        foreach (var responderGroup in responderGroups)
        {
            var groupResults = await Task.WhenAll(
                responderGroup.Select(async rt =>
                {
                    await using var serviceScope = _services.CreateAsyncScope();
                    try
                    {
                        var responder = (IResponder<TUpdate>)serviceScope.ServiceProvider.GetRequiredService(rt);
                        return await responder.RespondAsync(update, _responderCancellationSource.Token);
                    }
                    catch (Exception e)
                    {
                        return e;
                    }
                })).ConfigureAwait(false);

            results.AddRange(groupResults);
        }

        return results;
    }

    private Task<IReadOnlyList<Result>> LogAndReturnUnhandled(UpdateType updateType)
    {
        _logger.LogTrace(
            "Update of type {UpdateType} was received but not handled as no specific dispatcher was configured.",
            updateType);
        return Task.FromResult<IReadOnlyList<Result>>([]);
    }

    /// <summary>
    /// Finalizes the given dispatch results, logging any potential problems.
    /// </summary>
    /// <param name="responderResults">The results from the responder.</param>
    private void FinalizeResponderDispatch(IEnumerable<Result> responderResults)
    {
        try
        {
            foreach (var responderResult in responderResults)
            {
                if (responderResult.IsSuccess)
                {
                    continue;
                }

                switch (responderResult.Error)
                {
                    case ExceptionError exe:
                    {
                        if (exe.Exception is OperationCanceledException)
                        {
                            continue;
                        }

                        _logger.LogWarning
                        (
                            exe.Exception,
                            "Error in telegram event responder: {Exception}",
                            exe.Message
                        );

                        break;
                    }
                    default:
                    {
                        _logger.LogWarning
                        (
                            "Error in telegram event responder.\n{Reason}",
                            responderResult.Error!.Message
                        );

                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Pass, this is fine
        }
        catch (AggregateException aex)
        {
            foreach (var e in aex.InnerExceptions)
            {
                if (e is OperationCanceledException)
                {
                    continue;
                }

                _logger.LogWarning("Error in telegram event responder.\n{Exception}", e);
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning("Error in telegram event responder.\n{Exception}", e);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        GC.SuppressFinalize(this);

        // Signal running responders that they should cancel
#if NET8_0_OR_GREATER
        await _responderCancellationSource.CancelAsync();
#else
        _responderCancellationSource.Cancel();
#endif

        // Prevent further updates from being written, signaling the readers that they should terminate
        _updatesToDispatch.Writer.Complete();

        // Wait for everything to actually stop
        await _dispatcher;
        await _finalizer;

        _isDisposed = true;
    }
}