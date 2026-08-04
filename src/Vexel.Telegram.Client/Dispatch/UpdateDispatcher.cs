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
	/// Resolves handlers registered directly against <see cref="IRawUpdateHandler"/>, one
	/// registration at a time. The container materializes that set as a single unit, so one handler
	/// with unresolvable dependencies (an injected context that does not match the update kind, for
	/// example) would otherwise take every sibling with it. Scoped and transient registrations are
	/// built for this update and disposed with it; singleton registrations are built once and
	/// reused. <c>AddRawUpdateHandler&lt;THandler&gt;</c> stays the preferred raw path: it resolves
	/// straight from the container and keeps full lifetime, disposal, and decoration semantics.
	/// </summary>
	private ResolvedRawHandler[] ResolveContainerHandlers(IServiceProvider provider, Update update)
	{
		if (registry.ContainerRegistrations is { Count: > 0 } registrations)
		{
			return ResolvePerRegistration(registrations, provider, update);
		}

		// A registry built outside AddVexelTelegramClient has no registration snapshot, so the
		// container's set is the only view of these handlers.
		try
		{
			var handlers = provider.GetServices<IRawUpdateHandler>();
			return [.. handlers.Select(static handler => new ResolvedRawHandler(handler, Owned: false))];
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to resolve raw update handlers for update {UpdateId}", update.Id);
			return [];
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
			return new ResolvedRawHandler(
				registry.GetOrCreateSingletonHandler(descriptor, provider, CreateHandler),
				Owned: false);
		}

		return new ResolvedRawHandler(CreateHandler(descriptor, provider), Owned: true);
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

	/// <param name="Handler">The resolved raw handler.</param>
	/// <param name="Owned">
	/// True when the dispatcher constructed the handler outside the container and must dispose it.
	/// </param>
	private readonly record struct ResolvedRawHandler(IRawUpdateHandler Handler, bool Owned);
}
