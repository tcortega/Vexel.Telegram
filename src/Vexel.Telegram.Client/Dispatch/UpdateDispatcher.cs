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
			// Same invariant as an initializer fault below: without the bound context every handler
			// would fail on a half-bound scope, so the update must not be dispatched at all.
			logger.LogError(
				ex,
				"Failed to resolve update scope initializers for update {UpdateId}",
				update.Id);
			throw;
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

	/// <summary>
	/// Resolves handlers registered directly against <see cref="IRawUpdateHandler"/>. The container
	/// materializes that set as a unit, so one handler with unresolvable dependencies (an injected
	/// context that does not match the update kind, for example) would take every sibling with it.
	/// When the set fails, resolution degrades to one registration at a time so only the faulty
	/// handler is skipped. <c>AddRawUpdateHandler&lt;THandler&gt;</c> is the preferred raw path: it is
	/// isolated by construction and keeps full container lifetime semantics.
	/// </summary>
	private IRawUpdateHandler[] ResolveContainerHandlers(IServiceProvider provider, Update update)
	{
		try
		{
			return [.. provider.GetServices<IRawUpdateHandler>()];
		}
		catch (Exception ex)
		{
			logger.LogError(
				ex,
				"Failed to resolve the raw update handler set for update {UpdateId}; "
				+ "falling back to per-registration resolution",
				update.Id);

			return ResolveContainerHandlersPerRegistration(provider, update);
		}
	}

	private IRawUpdateHandler[] ResolveContainerHandlersPerRegistration(
		IServiceProvider provider,
		Update update)
	{
		var handlers = new List<IRawUpdateHandler>();

		foreach (var descriptor in registry.ContainerRegistrations)
		{
			try
			{
				handlers.Add(CreateContainerHandler(descriptor, provider));
			}
			catch (Exception ex)
			{
				logger.LogError(
					ex,
					"Failed to resolve container-registered raw update handler {HandlerType} for update {UpdateId}",
					descriptor.ImplementationInstance?.GetType().FullName
						?? descriptor.ImplementationType?.FullName
						?? typeof(IRawUpdateHandler).FullName,
					update.Id);
			}
		}

		return [.. handlers];
	}

	private static IRawUpdateHandler CreateContainerHandler(
		ServiceDescriptor descriptor,
		IServiceProvider provider)
	{
		if (descriptor.ImplementationInstance is IRawUpdateHandler instance)
		{
			return instance;
		}

		if (descriptor.ImplementationFactory is { } factory)
		{
			return (IRawUpdateHandler)factory(provider);
		}

		return (IRawUpdateHandler)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
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
