using Remora.Results;

namespace Vexel.Telegram.Abstractions.Responders;

/// <summary>
/// Represents a marker interface for event responders. This allows for easy discovery
/// of all responder implementations via dependency injection.
/// </summary>
public interface IResponder;

/// <summary>
/// Defines the contract for a type that responds to a specific Telegram update.
/// Responders are the core components for handling incoming events from Telegram.
/// </summary>
/// <typeparam name="TUpdate">The type of the Telegram update this responder handles.</typeparam>
/// <remarks>
/// Implementations of this interface should be registered in the dependency injection container
/// to be discoverable by the <see cref="IResponderDispatchService"/>. Each responder can handle one
/// or more update types by implementing the corresponding generic interface.
/// </remarks>
public interface IResponder<in TUpdate> : IResponder
{
	/// <summary>
	/// Responds to a given Telegram update.
	/// </summary>
	/// <param name="update">The update event to respond to.</param>
	/// <param name="ct">A cancellation token for the operation.</param>
	/// <returns>A <see cref="Task"/> that represents the asynchronous operation. The task result contains a <see cref="Result"/> indicating success or failure.</returns>
	Task<Result> RespondAsync(TUpdate update, CancellationToken ct = default);
}
