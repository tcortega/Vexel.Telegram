using Axon.Telegram.Commands.Contexts;
using Remora.Results;

namespace Axon.Telegram.Commands.Execution;

/// <summary>
/// Represents the public interface of a service that can perform a post-execution event.
/// </summary>
public interface IPostExecutionEvent
{
    /// <summary>
    /// Runs after a command has been executed, successfully or otherwise.
    /// </summary>
    /// <param name="context">The command context.</param>
    /// <param name="commandResult">The result returned by the command.</param>
    /// <param name="ct">The cancellation token of the current operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task<Result> AfterExecutionAsync
    (
        ICommandContext context,
        IResult commandResult,
        CancellationToken ct = default
    );
}