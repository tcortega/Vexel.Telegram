using Remora.Results;

namespace Axon.Telegram.Responders;

/// <summary>
/// Represents a type that responds to a given gateway event.
/// </summary>
/// <typeparam name="T">The type of event to respond to.</typeparam>
/// <remarks>
/// This is the Telegram equivalent of Remora.Discord's IResponder. It allows for handling
/// specific update types from Telegram (e.g., Message, CallbackQuery) in a decoupled manner.
/// </remarks>
public interface IResponder<in T>
{
    /// <summary>
    /// Responds to the given event.
    /// </summary>
    /// <param name="gatewayEvent">The event to respond to.</param>
    /// <param name="ct">The cancellation token for the operation.</param>
    /// <returns>A result that may or may not have succeeded.</returns>
    Task<Result> RespondAsync(T gatewayEvent, CancellationToken ct = default);
}