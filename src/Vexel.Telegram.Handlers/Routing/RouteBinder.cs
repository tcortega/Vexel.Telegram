namespace Vexel.Telegram.Handlers.Routing;

/// <summary>
/// Generated binder: build a request from the route payload, resolve the Immediate
/// <c>X.Handler</c> from the per-update scope, and await <c>HandleAsync</c>.
/// </summary>
/// <param name="scope">Per-update DI scope with contexts and Feedback already bound.</param>
/// <param name="payload">
/// For commands: message text after the command entity, trimmed (empty when the user sent only the command).
/// For callbacks: data suffix after the first <c>|</c> (empty string when absent).
/// </param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>
/// <see langword="true"/> when the handler was invoked; <see langword="false"/> when binding
/// failed (e.g. parse error) and the handler was skipped.
/// </returns>
public delegate ValueTask<bool> RouteBinder(
	IServiceProvider scope,
	string payload,
	CancellationToken cancellationToken);
