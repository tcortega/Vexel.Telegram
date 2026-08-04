using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;

namespace Vexel.Telegram.Client.Dispatch;

/// <summary>
/// Default update dispatcher.
/// T2 runs raw handlers only; later slices insert routed handlers and On* fan-out ahead of raw
/// (precedence: routed → On* → raw).
/// </summary>
public sealed class UpdateDispatcher(
	IServiceScopeFactory scopeFactory,
	IOptions<VexelClientOptions> options,
	ILogger<UpdateDispatcher> logger) : IUpdateDispatcher
{
	private readonly VexelClientOptions _options = options.Value;

	/// <inheritdoc />
	public async Task DispatchAsync(Update update, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(update);

		// One DI scope per update: scoped handler dependencies must not be captured by this
		// singleton nor shared concurrently across lanes.
		await using var scope = scopeFactory.CreateAsyncScope();

		IRawUpdateHandler[] handlers;
		try
		{
			handlers = [.. scope.ServiceProvider.GetServices<IRawUpdateHandler>()];
		}
		catch (Exception ex)
		{
			// A handler constructor or one of its dependencies faulted; keep it inside this update.
			logger.LogError(ex, "Failed to resolve raw update handlers for update {UpdateId}", update.Id);
			return;
		}

		// Routed handler + On* fan-out land in later slices; raw always runs last and cannot suppress routing.
		foreach (var handler in handlers)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await InvokeHandlerAsync(handler, update, cancellationToken).ConfigureAwait(false);
		}
	}

	private async Task InvokeHandlerAsync(
		IRawUpdateHandler handler,
		Update update,
		CancellationToken cancellationToken)
	{
		try
		{
			if (_options.HandlerTimeout is { } timeout)
			{
				using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				timeoutCts.CancelAfter(timeout);
				await handler.HandleAsync(update, timeoutCts.Token).ConfigureAwait(false);
			}
			else
			{
				await handler.HandleAsync(update, cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			// Fault isolation: one handler must not kill the lane or skip remaining handlers.
			// No automatic error text is sent to the chat (P2).
			logger.LogError(
				ex,
				"Raw update handler {HandlerType} failed for update {UpdateId}",
				handler.GetType().FullName,
				update.Id);
		}
	}
}
