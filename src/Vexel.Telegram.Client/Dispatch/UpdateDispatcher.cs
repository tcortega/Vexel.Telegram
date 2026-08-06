using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;

namespace Vexel.Telegram.Client.Dispatch;

/// <summary>
/// Default update dispatcher.
/// Precedence within a lane: routed handler → On* fan-out (inside <see cref="IUpdateRouter"/>)
/// → raw handlers → completion hooks.
/// </summary>
public sealed class UpdateDispatcher
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly RawUpdateHandlerRegistry _registry;
	private readonly IUpdateRouter? _router;
	private readonly IUpdateCompletionHook[] _completionHooks;
	private readonly VexelClientOptions _options;
	private readonly ILogger<UpdateDispatcher> _logger;

	/// <summary>
	/// Initializes a new dispatcher.
	/// </summary>
	/// <param name="scopeFactory">Factory for the per-update DI scope.</param>
	/// <param name="registry">Raw handler types registered via <c>AddRawUpdateHandler&lt;T&gt;</c>.</param>
	/// <param name="completionHooks">
	/// Post-pipeline hooks (answer obligations, etc.) run after routers and raw handlers, still in-scope.
	/// </param>
	/// <param name="options">Client options.</param>
	/// <param name="logger">Logger.</param>
	/// <param name="router">Optional routed + On* stage; absent when the app wires the client without routes.</param>
	internal UpdateDispatcher(
		IServiceScopeFactory scopeFactory,
		RawUpdateHandlerRegistry registry,
		IEnumerable<IUpdateCompletionHook> completionHooks,
		IOptions<VexelClientOptions> options,
		ILogger<UpdateDispatcher> logger,
		IUpdateRouter? router = null)
	{
		ArgumentNullException.ThrowIfNull(scopeFactory);
		ArgumentNullException.ThrowIfNull(registry);
		ArgumentNullException.ThrowIfNull(completionHooks);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(logger);

		_scopeFactory = scopeFactory;
		_registry = registry;
		_router = router;
		_completionHooks = completionHooks as IUpdateCompletionHook[] ?? [.. completionHooks];
		_options = options.Value;
		_logger = logger;
	}

	/// <summary>
	/// Runs the dispatch pipeline for one update.
	/// Handler exceptions are isolated per stage and never kill the scheduler lane.
	/// </summary>
	/// <param name="update">The inbound Telegram update.</param>
	/// <param name="cancellationToken">Token that signals dispatch should stop.</param>
	public async Task DispatchAsync(Update update, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(update);

		// One DI scope per update: scoped handler dependencies must not be captured by this
		// singleton nor shared concurrently across lanes.
		await using var scope = _scopeFactory.CreateAsyncScope();

		// Bind write-once update context (and later Feedback defaults) before any handler resolves.
		InitializeScope(scope.ServiceProvider, update);

		// Precedence: routed → On* (inside router) → raw → completion hooks.
		// Raw always runs after the router and cannot suppress routing.
		if (_router is not null)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await InvokeRouterAsync(_router, update, scope.ServiceProvider, cancellationToken)
				.ConfigureAwait(false);
		}

		foreach (var handlerType in _registry.HandlerTypes)
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
				_logger.LogError(
					ex,
					"Failed to resolve raw update handler {HandlerType} for update {UpdateId}",
					handlerType.FullName,
					update.Id);
				continue;
			}

			await InvokeHandlerAsync(handler, update, cancellationToken).ConfigureAwait(false);
		}

		// Fail-closed obligations (B4 callback/inline answers) run after every app stage so handlers
		// that answer themselves win, and unrouted updates still get a default answer.
		foreach (var hook in _completionHooks)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await InvokeCompletionHookAsync(hook, update, scope.ServiceProvider, cancellationToken)
				.ConfigureAwait(false);
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
			_logger.LogError(
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
				_logger.LogError(
					ex,
					"Update scope initializer {InitializerType} failed for update {UpdateId}",
					initializer.GetType().FullName,
					update.Id);
				throw;
			}
		}
	}

	private Task InvokeRouterAsync(
		IUpdateRouter updateRouter,
		Update update,
		IServiceProvider scope,
		CancellationToken cancellationToken) =>
		// Fault isolation: a router fault must not kill the lane or skip remaining stages.
		RunIsolatedAsync(
			token => updateRouter.RouteAsync(update, scope, token),
			"Update router",
			updateRouter.GetType(),
			update,
			cancellationToken);

	private Task InvokeCompletionHookAsync(
		IUpdateCompletionHook hook,
		Update update,
		IServiceProvider scope,
		CancellationToken cancellationToken) =>
		// Fault isolation: a completion hook must not kill the lane or skip remaining hooks.
		RunIsolatedAsync(
			token => hook.CompleteAsync(update, scope, token),
			"Update completion hook",
			hook.GetType(),
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
			_logger.LogError(
				ex,
				"{Stage} {StageType} failed for update {UpdateId}",
				stageName,
				stageType.FullName,
				update.Id);
		}
	}
}
