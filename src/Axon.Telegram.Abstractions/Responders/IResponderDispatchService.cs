using Remora.Results;
using Telegram.Bot.Types;

namespace Axon.Telegram.Abstractions.Responders;

/// <summary>
/// Defines the contract for a service that dispatches incoming Telegram updates to registered responders.
/// </summary>
public interface IResponderDispatchService
{
    /// <summary>
    /// Dispatches a given update to all relevant registered responders.
    /// </summary>
    /// <remarks>
    /// The dispatcher is responsible for identifying the concrete type of the update
    /// (e.g., Message, CallbackQuery) and invoking the <see cref="IResponder{TUpdate}.RespondAsync"/>
    /// method on all responders that handle that specific update type.
    /// </remarks>
    /// <param name="update">The <see cref="Update"/> object received from the Telegram Bot API.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>
    /// A <see cref="Task"/> that represents the asynchronous dispatch operation. The task result contains a <see cref="Result"/>
    /// which will be successful if dispatching was initiated, or contain an error if the dispatcher fails.
    /// Note that this does not reflect the success of individual responders.
    /// </returns>
    Task<Result> DispatchAsync(Update update, CancellationToken ct = default);
}