using Telegram.Bot.Types;

namespace Vexel.Telegram.Client;

/// <summary>
/// Escape hatch for handling Telegram updates outside routed handlers and On* fan-out.
/// Raw handlers run last for every update and cannot suppress routing.
/// </summary>
public interface IRawUpdateHandler
{
	/// <summary>
	/// Handles a single update.
	/// </summary>
	/// <param name="update">The inbound Telegram update.</param>
	/// <param name="cancellationToken">Token that signals the handler should stop.</param>
	/// <returns>A task that completes when handling finishes.</returns>
	Task HandleAsync(Update update, CancellationToken cancellationToken);
}
