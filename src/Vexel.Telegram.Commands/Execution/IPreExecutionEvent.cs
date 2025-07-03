using Vexel.Telegram.Commands.Contexts;
using Remora.Results;

namespace Vexel.Telegram.Commands.Execution;

/// <summary>
/// Represents the public interface of a service that can perform a pre-execution event.
/// </summary>
public interface IPreExecutionEvent
{
	/// <summary>
	/// Runs before the attempted execution of a command.
	/// </summary>
	/// <param name="context">The command context.</param>
	/// <param name="ct">The cancellation token of the current operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	Task<Result> BeforeExecutionAsync(ICommandContext context, CancellationToken ct = default);
}
