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
	RawUpdateHandlerRegistry registry,
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

		// Bind write-once update context (and later Feedback defaults) before any handler resolves.
		InitializeScope(scope.ServiceProvider, update);

		// Routed handler + On* fan-out land in later slices; raw always runs last and cannot suppress routing.
		foreach (var handlerType in registry.HandlerTypes)
		{
			cancellationToken.ThrowIfCancellationRequested();

			// Resolved one at a time so a faulty handler only costs itself.
			IRawUpdateHandler handler;
			try
			{
				handler = (IRawUpdateHandler)scope.ServiceProvider.GetRequiredService(handlerType);
			}
			catch (Exception ex)
			{
				logger.LogError(
					ex,
					"Failed to resolve raw update handler {HandlerType} for update {UpdateId}",
					handlerType.FullName,
					update.Id);
				continue;
			}

			await InvokeHandlerAsync(handler, update, cancellationToken).ConfigureAwait(false);
		}

		foreach (var handler in ResolveContainerHandlers(scope.ServiceProvider, update))
		{
			// The registry loop already ran this implementation; registering both ways is not two handlers.
			if (registry.Contains(handler.GetType()))
			{
				continue;
			}

			cancellationToken.ThrowIfCancellationRequested();
			await InvokeHandlerAsync(handler, update, cancellationToken).ConfigureAwait(false);
		}
	}

	private void InitializeScope(IServiceProvider provider, Update update)
	{
		IUpdateScopeInitializer[] initializers;
		try
		{
			initializers = [.. provider.GetServices<IUpdateScopeInitializer>()];
		}
		catch (Exception ex)
		{
			logger.LogError(
				ex,
				"Failed to resolve update scope initializers for update {UpdateId}",
				update.Id);
			return;
		}

		foreach (var initializer in initializers)
		{
			try
			{
				initializer.Initialize(update);
			}
			catch (Exception ex)
			{
				// Context binding failure is fatal for this update: handlers would see a half-bound scope.
				logger.LogError(
					ex,
					"Update scope initializer {InitializerType} failed for update {UpdateId}",
					initializer.GetType().FullName,
					update.Id);
				throw;
			}
		}
	}

	private IRawUpdateHandler[] ResolveContainerHandlers(IServiceProvider provider, Update update)
	{
		try
		{
			return [.. provider.GetServices<IRawUpdateHandler>()];
		}
		catch (Exception ex)
		{
			// The container builds this set as a unit, so one faulty handler fails all of them.
			logger.LogError(ex, "Failed to resolve raw update handlers for update {UpdateId}", update.Id);
			return [];
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
