using Telegram.Bot.Types;

namespace Vexel.Telegram.Client.Dispatch;

/// <summary>
/// Runs once per update after routers and raw handlers, still inside the per-update DI scope.
/// Used for fail-closed obligations such as answering callback/inline queries the app left hanging.
/// </summary>
public interface IUpdateCompletionHook
{
	/// <summary>
	/// Completes post-handler obligations for <paramref name="update"/>.
	/// </summary>
	/// <param name="update">The update that was dispatched.</param>
	/// <param name="scope">Per-update DI scope (contexts and Feedback still available).</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task CompleteAsync(Update update, IServiceProvider scope, CancellationToken cancellationToken);
}
