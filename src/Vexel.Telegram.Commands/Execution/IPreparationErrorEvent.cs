using Remora.Results;

namespace Vexel.Telegram.Commands;

/// <summary>
/// Represents the public interface of a service that can perform a command preparation error event.
/// </summary>
public interface IPreparationErrorEvent
{
	/// <summary>
	/// Runs when attempted preparation of a command fails.
	/// </summary>
	/// <param name="context">The operation context.</param>
	/// <param name="preparationResult">The result of the command preparation.</param>
	/// <param name="ct">The cancellation token of the current operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	Task<Result> PreparationFailed(IOperationContext context, IResult preparationResult,
		CancellationToken ct = default);
}
