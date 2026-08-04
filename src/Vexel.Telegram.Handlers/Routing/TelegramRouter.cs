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
/// Callback and inline answer obligations (B4) are discharged by
/// <see cref="CallbackAnswerObligation"/> and <see cref="InlineAnswerObligation"/> after the full
/// dispatch pipeline so routed handlers, exceptions, unrouted updates, and raw handlers all share
/// one fail-closed default answer when Feedback did not answer.
/// </remarks>
public sealed class TelegramRouter : IUpdateRouter
{
	private readonly ITelegramBotClient _botClient;
	private readonly ILogger<TelegramRouter> _logger;
	private readonly FrozenDictionary<string, RouteEntry> _commands;
	private readonly FrozenDictionary<string, RouteEntry> _callbacks;
	private readonly FrozenDictionary<string, RouteEntry> _inlineQueries;
	private readonly FrozenDictionary<string, RouteEntry> _chosenInlineResults;
	private string? _botUsername;
	private int _botUsernameResolved; // 0 = unset, 1 = resolved
	private long _botUsernameRetryAfterTicks;

	/// <summary>
	/// Initializes a new router from all registered <see cref="TelegramRouteContribution"/>s.
	/// Fails fast when two assemblies contribute the same command, callback, inline, or chosen key.
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
		_inlineQueries = ComposeInlineQueries(contributionList);
		_chosenInlineResults = ComposeChosenInlineResults(contributionList);
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

		if (update.InlineQuery is { } inlineQuery)
		{
			await RouteInlineQueryAsync(inlineQuery, update, scope, cancellationToken).ConfigureAwait(false);
			return;
		}

		if (update.ChosenInlineResult is { } chosenInlineResult)
		{
			await RouteChosenInlineResultAsync(chosenInlineResult, update, scope, cancellationToken)
				.ConfigureAwait(false);
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
		// so raw handlers can still answer unrouted callbacks first. Apps that handle callbacks
		// only through raw handlers are a supported shape, so a miss is not a warning.
		if (_callbacks.Count == 0)
		{
			return;
		}

		if (!CallbackKeyExtractor.TryExtract(callbackQuery.Data, out var key, out var suffix))
		{
			_logger.LogDebug(
				"Unrouted callback query {CallbackId} for update {UpdateId}: no callback data",
				callbackQuery.Id,
				update.Id);
			return;
		}

		if (!_callbacks.TryGetValue(key, out var entry))
		{
			_logger.LogDebug(
				"Unrouted callback query {CallbackId} for update {UpdateId}: no route for key '{CallbackKey}'",
				callbackQuery.Id,
				update.Id,
				key);
			return;
		}

		await InvokeRouteAsync(
			entry,
			suffix,
			update,
			scope,
			RouteKindLabel.Callback,
			cancellationToken).ConfigureAwait(false);
	}

	private async Task RouteInlineQueryAsync(
		InlineQuery inlineQuery,
		Update update,
		IServiceProvider scope,
		CancellationToken cancellationToken)
	{
		// Routing keys off query text only - never InlineQuery.Id (opaque Telegram server id).
		// Answer obligation (B4) is discharged by InlineAnswerObligation after the full pipeline.
		if (_inlineQueries.Count == 0)
		{
			return;
		}

		var (trigger, remainder) = InlineQueryKeyExtractor.Extract(inlineQuery.Query);

		// Non-empty first token that matches a registered trigger wins (D8 exact match).
		// The empty-string default is never selected via the trigger path.
		if (trigger.Length > 0
			&& _inlineQueries.TryGetValue(trigger, out var triggerEntry))
		{
			await InvokeRouteAsync(
				triggerEntry,
				remainder,
				update,
				scope,
				RouteKindLabel.InlineQuery,
				cancellationToken).ConfigureAwait(false);
			return;
		}

		// Empty or unmatched queries route to the default handler with the full query text.
		if (_inlineQueries.TryGetValue(string.Empty, out var defaultEntry))
		{
			await InvokeRouteAsync(
				defaultEntry,
				inlineQuery.Query ?? string.Empty,
				update,
				scope,
				RouteKindLabel.InlineQuery,
				cancellationToken).ConfigureAwait(false);
			return;
		}

		_logger.LogDebug(
			"Unrouted inline query {InlineQueryId} for update {UpdateId}: no default handler and no trigger '{Trigger}'",
			inlineQuery.Id,
			update.Id,
			trigger);
	}

	private async Task RouteChosenInlineResultAsync(
		ChosenInlineResult chosenInlineResult,
		Update update,
		IServiceProvider scope,
		CancellationToken cancellationToken)
	{
		if (_chosenInlineResults.Count == 0)
		{
			return;
		}

		// Same key|suffix convention as callbacks against the developer-set ResultId.
		if (!CallbackKeyExtractor.TryExtract(chosenInlineResult.ResultId, out var key, out var suffix))
		{
			_logger.LogDebug(
				"Unrouted chosen inline result for update {UpdateId}: empty ResultId",
				update.Id);
			return;
		}

		if (!_chosenInlineResults.TryGetValue(key, out var entry))
		{
			_logger.LogDebug(
				"Unrouted chosen inline result for update {UpdateId}: no route for key '{ResultKey}'",
				update.Id,
				key);
			return;
		}

		await InvokeRouteAsync(
			entry,
			suffix,
			update,
			scope,
			RouteKindLabel.ChosenInlineResult,
			cancellationToken).ConfigureAwait(false);
	}

	private async Task InvokeRouteAsync(
		RouteEntry entry,
		string payload,
		Update update,
		IServiceProvider scope,
		RouteKindLabel kind,
		CancellationToken cancellationToken)
	{
		bool invoked;
		try
		{
			invoked = await entry.Binder(scope, payload, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			// Fault isolation: handler exceptions never kill the lane (P2). Answer obligations
			// still discharge post-pipeline. No auto error text.
			_logger.LogError(
				ex,
				"{Kind} handler for '{RouteKey}' failed for update {UpdateId}",
				kind.Title,
				entry.RouteKey,
				update.Id);
			return;
		}

		if (!invoked)
		{
			_logger.LogWarning(
				"Failed to bind arguments for {Kind} '{RouteKey}' (update {UpdateId}); handler not invoked",
				kind.Lowercase,
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
		IEnumerable<TelegramRouteContribution> contributions) =>
		ComposeOrdinalMap(contributions, static c => c.Callbacks, "callback");

	private static FrozenDictionary<string, RouteEntry> ComposeInlineQueries(
		IEnumerable<TelegramRouteContribution> contributions) =>
		ComposeOrdinalMap(contributions, static c => c.InlineQueries, "inline query");

	private static FrozenDictionary<string, RouteEntry> ComposeChosenInlineResults(
		IEnumerable<TelegramRouteContribution> contributions) =>
		ComposeOrdinalMap(contributions, static c => c.ChosenInlineResults, "chosen inline result");

	private static FrozenDictionary<string, RouteEntry> ComposeOrdinalMap(
		IEnumerable<TelegramRouteContribution> contributions,
		Func<TelegramRouteContribution, IReadOnlyDictionary<string, RouteBinder>> selector,
		string kind)
	{
		var map = new Dictionary<string, RouteEntry>(StringComparer.Ordinal);
		var owners = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var contribution in contributions)
		{
			foreach (var pair in selector(contribution))
			{
				if (owners.TryGetValue(pair.Key, out var otherAssembly))
				{
					var displayKey = pair.Key.Length == 0 ? "<default>" : pair.Key;
					throw new InvalidOperationException(
						string.Equals(otherAssembly, contribution.AssemblyName, StringComparison.Ordinal)
							? $"Assembly '{contribution.AssemblyName}' contributed {kind} route "
								+ $"'{displayKey}' more than once. Call Add{contribution.AssemblyName}Telegram() "
								+ "exactly once (a library that already calls it must not be re-registered by the app)."
							: $"Duplicate {kind} route '{displayKey}' registered by assemblies "
								+ $"'{otherAssembly}' and '{contribution.AssemblyName}'.");
				}

				owners[pair.Key] = contribution.AssemblyName;
				map[pair.Key] = new RouteEntry(pair.Key, pair.Value);
			}
		}

		return map.ToFrozenDictionary(StringComparer.Ordinal);
	}

	private readonly record struct RouteEntry(string RouteKey, RouteBinder Binder);

	/// <summary>Sentence-initial and mid-sentence spellings of a route kind for log templates.</summary>
	private readonly record struct RouteKindLabel(string Title, string Lowercase)
	{
		public static RouteKindLabel Callback { get; } = new("Callback", "callback");

		public static RouteKindLabel InlineQuery { get; } = new("Inline query", "inline query");

		public static RouteKindLabel ChosenInlineResult { get; } =
			new("Chosen inline result", "chosen inline result");
	}
}
