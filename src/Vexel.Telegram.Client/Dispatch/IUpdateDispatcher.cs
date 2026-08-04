using Telegram.Bot.Types;

namespace Vexel.Telegram.Client.Dispatch;

/// <summary>
/// Dispatches a single update through the Vexel pipeline.
/// Precedence within a lane: routed handler → On* fan-out → <see cref="IRawUpdateHandler"/>s.
/// </summary>
public interface IUpdateDispatcher
{
	/// <summary>
	/// Runs the dispatch pipeline for one update.
	/// Implementations must not let a handler exception escape in a way that kills the scheduler lane;
	/// the default dispatcher isolates per handler and logs failures.
	/// </summary>
	/// <param name="update">The inbound Telegram update.</param>
	/// <param name="cancellationToken">Token that signals dispatch should stop.</param>
	/// <returns>A task that completes when the pipeline finishes.</returns>
	Task DispatchAsync(Update update, CancellationToken cancellationToken);
}
