using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Vexel.Telegram.Client.Dispatch;

namespace Vexel.Telegram.Handlers.Routing;

/// <summary>
/// Runtime Telegram router: classify the update, extract a route key per the binding convention,
/// bind arguments, and await the generated Immediate handler.
/// </summary>
/// <remarks>
/// Callback answer obligation (B4) is discharged by <see cref="CallbackAnswerObligation"/> after
/// the full dispatch pipeline so routed handlers, exceptions, unrouted callbacks, and raw handlers
/// all share one fail-closed empty <c>answerCallbackQuery</c> when Feedback did not answer.
/// </remarks>
public sealed class TelegramRouter : IUpdateRouter
{
	private readonly ITelegramBotClient _botClient;
	private readonly ILogger<TelegramRouter> _logger;
	private readonly FrozenDictionary<string, RouteEntry> _commands;
	private readonly FrozenDictionary<string, RouteEntry> _callbacks;
	private string? _botUsername;
	private int _botUsernameResolved; // 0 = unset, 1 = resolved
	private long _botUsernameRetryAfterTicks;

	/// <summary>
	/// Initializes a new router from all registered <see cref="TelegramRouteContribution"/>s.
	/// Fails fast when two assemblies contribute the same command or callback key.
	/// </summary>
	/// <param name="contributions">Per-assembly route contributions.</param>
	/// <param name="botClient">Bot client used to resolve this bot's username when needed.</param>
	/// <param name="logger">Logger.</param>
	public TelegramRouter(
		IEnumerable<TelegramRouteContribution> contributions,
		ITelegramBotClient botClient,
		ILogger<TelegramRouter> logger)
	{
		ArgumentNullException.ThrowIfNull(contributions);
		ArgumentNullException.ThrowIfNull(botClient);
		ArgumentNullException.ThrowIfNull(logger);

		_botClient = botClient;
		_logger = logger;

		var contributionList = contributions as IList<TelegramRouteContribution> ?? [.. contributions];
		_commands = ComposeCommands(contributionList);
		_callbacks = ComposeCallbacks(contributionList);
	}

	/// <summary>
	/// Optional bot username override (without <c>@</c>). When set, skips <c>GetMe</c>.
	/// Intended for tests.
	/// </summary>
	public string? BotUsernameOverride
	{
		get => _botUsername;
		set
		{
			_botUsername = value;
			Volatile.Write(ref _botUsernameResolved, 1);
		}
	}

	/// <summary>
	/// How long to wait before retrying <c>GetMe</c> after a failed bot-username lookup. Keeps a
	/// persistently failing lookup off the per-update path without disabling <c>@suffix</c> matching
	/// for the process lifetime.
	/// </summary>
	public TimeSpan BotUsernameRetryBackoff { get; set; } = TimeSpan.FromSeconds(30);

	/// <summary>Composed command metadata across all contributions (for SetMyCommands).</summary>
	public IReadOnlyList<CommandRouteMetadata> CommandMetadata { get; private set; } = [];

	/// <inheritdoc />
	public async Task RouteAsync(Update update, IServiceProvider scope, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(update);
		ArgumentNullException.ThrowIfNull(scope);

		if (update.CallbackQuery is { } callbackQuery)
		{
			await RouteCallbackAsync(callbackQuery, update, scope, cancellationToken).ConfigureAwait(false);
			return;
		}

		if (update.Message is not { } message || _commands.Count == 0)
		{
			return;
		}

		if (!CommandKeyExtractor.TryExtract(message, out var command, out var botSuffix, out var arguments))
		{
			return;
		}

		// GetMe is only needed to adjudicate an @Suffix, so plain commands never pay for it.
		if (botSuffix is not null)
		{
			var botUsername = await ResolveBotUsernameAsync(cancellationToken).ConfigureAwait(false);
			if (botUsername is not null
				&& !botSuffix.Equals(botUsername, StringComparison.OrdinalIgnoreCase))
			{
				// Directed at another bot - fall through to flow/On*.
				return;
			}
		}

		if (!_commands.TryGetValue(command, out var entry))
		{
			return;
		}

		bool invoked;
		try
		{
			invoked = await entry.Binder(scope, arguments, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			// Fault isolation: handler exceptions never kill the lane (P2). No auto error text.
			_logger.LogError(
				ex,
				"Command handler for /{Command} failed for update {UpdateId}",
				entry.RouteKey,
				update.Id);
			return;
		}

		if (!invoked)
		{
			_logger.LogWarning(
				"Failed to bind arguments for /{Command} (update {UpdateId}); handler not invoked",
				entry.RouteKey,
				update.Id);
		}
	}

	private async Task RouteCallbackAsync(
		CallbackQuery callbackQuery,
		Update update,
		IServiceProvider scope,
		CancellationToken cancellationToken)
	{
		// Answer obligation (B4) is discharged by CallbackAnswerObligation after the full pipeline
		// so raw handlers can still answer unrouted callbacks first.
		if (!CallbackKeyExtractor.TryExtract(callbackQuery.Data, out var key, out var suffix))
		{
			_logger.LogWarning(
				"Unrouted callback query {CallbackId} for update {UpdateId}: no callback data",
				callbackQuery.Id,
				update.Id);
			return;
		}

		if (!_callbacks.TryGetValue(key, out var entry))
		{
			_logger.LogWarning(
				"Unrouted callback query {CallbackId} for update {UpdateId}: no route for key '{CallbackKey}'",
				callbackQuery.Id,
				update.Id,
				key);
			return;
		}

		bool invoked;
		try
		{
			invoked = await entry.Binder(scope, suffix, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			// Fault isolation: handler exceptions never kill the lane (P2). Answer obligation
			// still discharges post-pipeline. No auto error text.
			_logger.LogError(
				ex,
				"Callback handler for '{CallbackKey}' failed for update {UpdateId}",
				entry.RouteKey,
				update.Id);
			return;
		}

		if (!invoked)
		{
			_logger.LogWarning(
				"Failed to bind arguments for callback '{CallbackKey}' (update {UpdateId}); handler not invoked",
				entry.RouteKey,
				update.Id);
		}
	}

	private async ValueTask<string?> ResolveBotUsernameAsync(CancellationToken cancellationToken)
	{
		if (Volatile.Read(ref _botUsernameResolved) == 1)
		{
			return _botUsername;
		}

		// A transient GetMe failure must not disable @suffix matching for the process lifetime, so the
		// resolved flag latches on success only. The backoff keeps a persistent failure from putting a
		// GetMe round-trip on every suffixed command.
		if (Environment.TickCount64 < Volatile.Read(ref _botUsernameRetryAfterTicks))
		{
			return null;
		}

		try
		{
			var me = await _botClient.GetMe(cancellationToken).ConfigureAwait(false);
			_botUsername = me.Username;
			Volatile.Write(ref _botUsernameResolved, 1);
			return _botUsername;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			Volatile.Write(ref _botUsernameRetryAfterTicks, Environment.TickCount64 + (long)BotUsernameRetryBackoff.TotalMilliseconds);
			throw;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(
				ex,
				"Failed to resolve bot username via GetMe; @suffix matching disabled until the next retry");
			Volatile.Write(ref _botUsernameRetryAfterTicks, Environment.TickCount64 + (long)BotUsernameRetryBackoff.TotalMilliseconds);
			return null;
		}
	}

	private FrozenDictionary<string, RouteEntry> ComposeCommands(
		IEnumerable<TelegramRouteContribution> contributions)
	{
		var map = new Dictionary<string, RouteEntry>(StringComparer.OrdinalIgnoreCase);
		var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var metadata = new List<CommandRouteMetadata>();

		foreach (var contribution in contributions)
		{
			foreach (var pair in contribution.Commands)
			{
				if (owners.TryGetValue(pair.Key, out var otherAssembly))
				{
					throw new InvalidOperationException(
						string.Equals(otherAssembly, contribution.AssemblyName, StringComparison.Ordinal)
							? $"Assembly '{contribution.AssemblyName}' contributed command route "
								+ $"'{pair.Key}' more than once. Call Add{contribution.AssemblyName}Telegram() "
								+ "exactly once (a library that already calls it must not be re-registered by the app)."
							: $"Duplicate command route '{pair.Key}' registered by assemblies "
								+ $"'{otherAssembly}' and '{contribution.AssemblyName}'.");
				}

				owners[pair.Key] = contribution.AssemblyName;
				map[pair.Key] = new RouteEntry(pair.Key, pair.Value);
			}

			metadata.AddRange(contribution.CommandMetadata);
		}

		CommandMetadata = [.. metadata.OrderBy(static m => m.Name, StringComparer.Ordinal)];
		return map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
	}

	private static FrozenDictionary<string, RouteEntry> ComposeCallbacks(
		IEnumerable<TelegramRouteContribution> contributions)
	{
		var map = new Dictionary<string, RouteEntry>(StringComparer.Ordinal);
		var owners = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var contribution in contributions)
		{
			foreach (var pair in contribution.Callbacks)
			{
				if (owners.TryGetValue(pair.Key, out var otherAssembly))
				{
					throw new InvalidOperationException(
						string.Equals(otherAssembly, contribution.AssemblyName, StringComparison.Ordinal)
							? $"Assembly '{contribution.AssemblyName}' contributed callback route "
								+ $"'{pair.Key}' more than once. Call Add{contribution.AssemblyName}Telegram() "
								+ "exactly once (a library that already calls it must not be re-registered by the app)."
							: $"Duplicate callback route '{pair.Key}' registered by assemblies "
								+ $"'{otherAssembly}' and '{contribution.AssemblyName}'.");
				}

				owners[pair.Key] = contribution.AssemblyName;
				map[pair.Key] = new RouteEntry(pair.Key, pair.Value);
			}
		}

		return map.ToFrozenDictionary(StringComparer.Ordinal);
	}

	private readonly record struct RouteEntry(string RouteKey, RouteBinder Binder);
}
