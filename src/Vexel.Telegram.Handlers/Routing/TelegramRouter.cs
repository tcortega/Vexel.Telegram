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
public sealed class TelegramRouter : IUpdateRouter
{
	private readonly ITelegramBotClient _botClient;
	private readonly ILogger<TelegramRouter> _logger;
	private readonly FrozenDictionary<string, RouteEntry> _commands;
	private string? _botUsername;
	private int _botUsernameResolved; // 0 = unset, 1 = resolved

	/// <summary>
	/// Initializes a new router from all registered <see cref="TelegramRouteContribution"/>s.
	/// Fails fast when two assemblies contribute the same command key.
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
		_commands = ComposeCommands(contributions);
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

	/// <summary>Composed command metadata across all contributions (for SetMyCommands).</summary>
	public IReadOnlyList<CommandRouteMetadata> CommandMetadata { get; private set; } = [];

	/// <inheritdoc />
	public async Task RouteAsync(Update update, IServiceProvider scope, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(update);
		ArgumentNullException.ThrowIfNull(scope);

		if (update.Message is not { } message || _commands.Count == 0)
		{
			return;
		}

		var botUsername = await ResolveBotUsernameAsync(cancellationToken).ConfigureAwait(false);
		if (!CommandKeyExtractor.TryExtract(message, botUsername, out var command, out var arguments))
		{
			return;
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
				entry.CommandName,
				update.Id);
			return;
		}

		if (!invoked)
		{
			_logger.LogWarning(
				"Failed to bind arguments for /{Command} (update {UpdateId}); handler not invoked",
				entry.CommandName,
				update.Id);
		}
	}

	private async ValueTask<string?> ResolveBotUsernameAsync(CancellationToken cancellationToken)
	{
		if (Volatile.Read(ref _botUsernameResolved) == 1)
		{
			return _botUsername;
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
			throw;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to resolve bot username via GetMe; @suffix matching disabled");
			_botUsername = null;
			Volatile.Write(ref _botUsernameResolved, 1);
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
						$"Duplicate command route '{pair.Key}' registered by assemblies "
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

	private readonly record struct RouteEntry(string CommandName, RouteBinder Binder);
}
