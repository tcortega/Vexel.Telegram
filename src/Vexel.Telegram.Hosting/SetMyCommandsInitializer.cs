using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Vexel.Telegram.Client;

namespace Vexel.Telegram.Hosting;

/// <summary>
/// Registers bot commands via <c>setMyCommands</c> from the composed route catalog at host start.
/// Opt out with <see cref="VexelClientOptions.RegisterBotCommands"/> = <see langword="false"/>.
/// </summary>
public sealed class SetMyCommandsInitializer(
	ITelegramBotClient botClient,
	IOptions<VexelClientOptions> options,
	ILogger<SetMyCommandsInitializer> logger,
	IBotCommandCatalog? catalog = null) : IHostedService
{
	/// <inheritdoc />
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		if (!options.Value.RegisterBotCommands)
		{
			logger.LogInformation("SetMyCommands skipped (RegisterBotCommands=false).");
			return;
		}

		if (catalog is null)
		{
			// Client-only wiring (no router): leave whatever Telegram already has alone.
			logger.LogDebug("SetMyCommands skipped (no IBotCommandCatalog registered).");
			return;
		}

		try
		{
			var payload = BuildPayload(catalog.Commands);
			if (payload.Count == 0)
			{
				// An empty setMyCommands payload deletes the menu; leave whatever Telegram already has alone.
				logger.LogDebug("SetMyCommands skipped (catalog contributed no commands).");
				return;
			}

			await botClient.SetMyCommands(payload, cancellationToken: cancellationToken).ConfigureAwait(false);

			logger.LogInformation("Registered {Count} bot command(s) with Telegram (setMyCommands).", payload.Count);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			// Command metadata is cosmetic: a bad catalog or a Telegram-side failure must never
			// turn into a failed host start.
			logger.LogError(ex, "Failed to register bot commands with Telegram (setMyCommands).");
		}
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	/// <summary>
	/// Converts catalog descriptors into Telegram <see cref="BotCommand"/> values.
	/// Empty descriptions fall back to the command name so the API's non-empty description
	/// rule is always satisfied (names are already 1-32 chars by generator validation).
	/// </summary>
	internal static IReadOnlyList<BotCommand> BuildPayload(IEnumerable<BotCommandDescriptor> commands)
	{
		ArgumentNullException.ThrowIfNull(commands);

		var payload = new List<BotCommand>();
		foreach (var command in commands)
		{
			ArgumentNullException.ThrowIfNull(command);
			ArgumentException.ThrowIfNullOrWhiteSpace(command.Name);

			var description = string.IsNullOrWhiteSpace(command.Description)
				? command.Name
				: command.Description.Trim();

			payload.Add(new BotCommand
			{
				Command = command.Name,
				Description = description,
			});
		}

		return payload;
	}
}
