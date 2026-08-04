using System.Collections.Concurrent;
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
/// <param name="scopeFactory">Factory for the per-update DI scope.</param>
/// <param name="rootProvider">
/// Root provider. Used only to build singleton raw handlers in the degraded resolution path, so
/// they never capture an update scope and stay subject to the container's scope validation.
/// </param>
/// <param name="registry">Raw handler registry.</param>
/// <param name="options">Client options.</param>
/// <param name="logger">Logger.</param>
public sealed class UpdateDispatcher(
	IServiceScopeFactory scopeFactory,
	IServiceProvider rootProvider,
	RawUpdateHandlerRegistry registry,
	IEnumerable<IUpdateRouter> routers,
	IOptions<VexelClientOptions> options,
	ILogger<UpdateDispatcher> logger) : IUpdateDispatcher, IDisposable, IAsyncDisposable
{
	private readonly VexelClientOptions _options = options.Value;
	private readonly IUpdateRouter[] _routers = routers as IUpdateRouter[] ?? [.. routers];
	private readonly ConcurrentDictionary<ServiceDescriptor, Lazy<IRawUpdateHandler>> _singletonHandlers = new();

	/// <inheritdoc />
	public async Task DispatchAsync(Update update, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(update);

		// One DI scope per update: scoped handler dependencies must not be captured by this
		// singleton nor shared concurrently across lanes.
		await using var scope = scopeFactory.CreateAsyncScope();

		// Bind write-once update context (and later Feedback defaults) before any handler resolves.
		InitializeScope(scope.ServiceProvider, update);

		// Precedence: routed → On* (later) → raw. Raw always runs last and cannot suppress routing.
		foreach (var router in _routers)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await InvokeRouterAsync(router, update, scope.ServiceProvider, cancellationToken)
				.ConfigureAwait(false);
		}

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

		var containerHandlers = ResolveContainerHandlers(scope.ServiceProvider, update);
		try
		{
			foreach (var resolved in containerHandlers)
			{
				// The registry loop already ran this implementation; registering both ways is not two handlers.
				if (registry.Contains(resolved.Handler.GetType()))
				{
					continue;
				}

				cancellationToken.ThrowIfCancellationRequested();
				await InvokeHandlerAsync(resolved.Handler, update, cancellationToken).ConfigureAwait(false);
			}
		}
		finally
		{
			await DisposeOwnedHandlersAsync(containerHandlers, update).ConfigureAwait(false);
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
	/// is asked for the whole set first, which keeps per-scope identity, decorators, and
	/// registrations made straight on a third-party container intact. The container builds that set
	/// as a single unit though, so one handler with unresolvable dependencies (an injected context
	/// that does not match the update kind, for example) takes every sibling with it; only then does
	/// resolution degrade to one registration at a time so the faulty handler is the only one
	/// skipped. In that degraded path scoped and transient registrations are built for this update
	/// and disposed with it, and singleton registrations are built once from the root provider.
	/// <c>AddRawUpdateHandler&lt;THandler&gt;</c> stays the preferred raw path: it always resolves
	/// straight from the container, one handler at a time.
	/// </summary>
	private ResolvedRawHandler[] ResolveContainerHandlers(IServiceProvider provider, Update update)
	{
		try
		{
			var handlers = provider.GetServices<IRawUpdateHandler>();
			return [.. handlers.Select(static handler => new ResolvedRawHandler(handler, Owned: false))];
		}
		catch (Exception ex)
		{
			logger.LogError(
				ex,
				"Failed to resolve the raw update handler set for update {UpdateId}; "
				+ "falling back to per-registration resolution",
				update.Id);

			return ResolvePerRegistration(registry.ContainerRegistrations, provider, update);
		}
	}

	private ResolvedRawHandler[] ResolvePerRegistration(
		IReadOnlyList<ServiceDescriptor> registrations,
		IServiceProvider provider,
		Update update)
	{
		var handlers = new List<ResolvedRawHandler>(registrations.Count);

		foreach (var descriptor in registrations)
		{
			// Dedup before construction: the registry loop already owns this implementation.
			if (descriptor.ImplementationType is { } implementationType
				&& registry.Contains(implementationType))
			{
				continue;
			}

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

	private ResolvedRawHandler CreateContainerHandler(ServiceDescriptor descriptor, IServiceProvider provider)
	{
		if (descriptor.ImplementationInstance is IRawUpdateHandler instance)
		{
			return new ResolvedRawHandler(instance, Owned: false);
		}

		if (descriptor.Lifetime == ServiceLifetime.Singleton)
		{
			return new ResolvedRawHandler(GetOrCreateSingletonHandler(descriptor), Owned: false);
		}

		return new ResolvedRawHandler(CreateHandler(descriptor, provider), Owned: true);
	}

	private IRawUpdateHandler GetOrCreateSingletonHandler(ServiceDescriptor descriptor)
	{
		// Built from the root provider, never from the update scope: a singleton must not capture
		// this update's contexts, and the root provider still applies the container's scope validation.
		var lazy = _singletonHandlers.GetOrAdd(
			descriptor,
			static (key, root) => new Lazy<IRawUpdateHandler>(() => CreateHandler(key, root)),
			rootProvider);

		try
		{
			return lazy.Value;
		}
		catch
		{
			_ = _singletonHandlers.TryRemove(
				new KeyValuePair<ServiceDescriptor, Lazy<IRawUpdateHandler>>(descriptor, lazy));
			throw;
		}
	}

	private static IRawUpdateHandler CreateHandler(ServiceDescriptor descriptor, IServiceProvider provider) =>
		descriptor.ImplementationFactory is { } factory
			? (IRawUpdateHandler)factory(provider)
			: (IRawUpdateHandler)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);

	private async ValueTask DisposeOwnedHandlersAsync(ResolvedRawHandler[] handlers, Update update)
	{
		foreach (var resolved in handlers)
		{
			if (!resolved.Owned)
			{
				continue;
			}

			try
			{
				if (resolved.Handler is IAsyncDisposable asyncDisposable)
				{
					await asyncDisposable.DisposeAsync().ConfigureAwait(false);
				}
				else if (resolved.Handler is IDisposable disposable)
				{
					disposable.Dispose();
				}
			}
			catch (Exception ex)
			{
				logger.LogError(
					ex,
					"Failed to dispose raw update handler {HandlerType} for update {UpdateId}",
					resolved.Handler.GetType().FullName,
					update.Id);
			}
		}
	}

	private Task InvokeRouterAsync(
		IUpdateRouter router,
		Update update,
		IServiceProvider scope,
		CancellationToken cancellationToken) =>
		// Fault isolation: a router fault must not kill the lane or skip remaining stages.
		RunIsolatedAsync(
			token => router.RouteAsync(update, scope, token),
			"Update router",
			router.GetType(),
			update,
			cancellationToken);

	private Task InvokeHandlerAsync(
		IRawUpdateHandler handler,
		Update update,
		CancellationToken cancellationToken) =>
		// Fault isolation: one handler must not kill the lane or skip remaining handlers.
		// No automatic error text is sent to the chat (P2).
		RunIsolatedAsync(
			token => handler.HandleAsync(update, token),
			"Raw update handler",
			handler.GetType(),
			update,
			cancellationToken);

	/// <summary>
	/// Runs one dispatch stage under the shared timeout and fault-isolation contract: the configured
	/// <see cref="VexelClientOptions.HandlerTimeout"/> bounds the stage, cancellation of the lane's own
	/// token still propagates, and every other fault is logged and swallowed so the lane survives.
	/// </summary>
	private async Task RunIsolatedAsync(
		Func<CancellationToken, Task> body,
		string stageName,
		Type stageType,
		Update update,
		CancellationToken cancellationToken)
	{
		try
		{
			if (_options.HandlerTimeout is { } timeout)
			{
				using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				timeoutCts.CancelAfter(timeout);
				await body(timeoutCts.Token).ConfigureAwait(false);
			}
			else
			{
				await body(cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			logger.LogError(
				ex,
				"{Stage} {StageType} failed for update {UpdateId}",
				stageName,
				stageType.FullName,
				update.Id);
		}
	}

	/// <summary>
	/// Disposes the singleton raw handlers this dispatcher built outside the container. The
	/// container disposes the dispatcher itself, so these follow the provider's lifetime.
	/// </summary>
	public void Dispose()
	{
		foreach (var handler in DrainSingletonHandlers())
		{
			try
			{
				(handler as IDisposable)?.Dispose();
			}
			catch (Exception ex)
			{
				logger.LogError(
					ex,
					"Failed to dispose singleton raw update handler {HandlerType}",
					handler.GetType().FullName);
			}
		}
	}

	/// <inheritdoc cref="Dispose" />
	public async ValueTask DisposeAsync()
	{
		foreach (var handler in DrainSingletonHandlers())
		{
			try
			{
				if (handler is IAsyncDisposable asyncDisposable)
				{
					await asyncDisposable.DisposeAsync().ConfigureAwait(false);
				}
				else if (handler is IDisposable disposable)
				{
					disposable.Dispose();
				}
			}
			catch (Exception ex)
			{
				logger.LogError(
					ex,
					"Failed to dispose singleton raw update handler {HandlerType}",
					handler.GetType().FullName);
			}
		}
	}

	private List<IRawUpdateHandler> DrainSingletonHandlers()
	{
		var handlers = new List<IRawUpdateHandler>(_singletonHandlers.Count);

		foreach (var descriptor in _singletonHandlers.Keys)
		{
			if (_singletonHandlers.TryRemove(descriptor, out var lazy) && lazy.IsValueCreated)
			{
				handlers.Add(lazy.Value);
			}
		}

		return handlers;
	}

	/// <param name="Handler">The resolved raw handler.</param>
	/// <param name="Owned">
	/// True when the dispatcher constructed the handler outside the container and must dispose it.
	/// </param>
	private readonly record struct ResolvedRawHandler(IRawUpdateHandler Handler, bool Owned);
}
