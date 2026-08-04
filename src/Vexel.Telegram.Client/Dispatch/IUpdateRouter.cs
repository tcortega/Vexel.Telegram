using Telegram.Bot.Types;

namespace Vexel.Telegram.Client.Dispatch;

/// <summary>
/// Optional routed-handler stage of the update pipeline.
/// Runs after scope initialization and before On* fan-out and <see cref="IRawUpdateHandler"/>s.
/// </summary>
public interface IUpdateRouter
{
	/// <summary>
	/// Attempts to dispatch a compile-time routed handler for <paramref name="update"/>.
	/// Implementations must isolate handler faults so a thrown handler cannot kill the lane.
	/// </summary>
	/// <param name="update">The inbound Telegram update.</param>
	/// <param name="scope">The per-update DI scope (contexts and Feedback already bound).</param>
	/// <param name="cancellationToken">Token that signals dispatch should stop.</param>
	/// <returns>A task that completes when routing for this update finishes.</returns>
	Task RouteAsync(Update update, IServiceProvider scope, CancellationToken cancellationToken);
}
