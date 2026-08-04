using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Vexel.Telegram.Client;

namespace Vexel.Telegram.Hosting;

/// <summary>
/// Registers bot commands via <c>setMyCommands</c> from the composed route catalog at host start.
/// Opt out with <see cref="VexelClientOptions.RegisterBotCommands"/> = <see langword="false"/>.
/// </summary>
public sealed class SetMyCommandsInitializer(
	ITelegramBotClient botClient,
	IOptions<VexelClientOptions> options,
	IEnumerable<IBotCommandCatalog> catalogs,
	ILogger<SetMyCommandsInitializer> logger) : IHostedService
{
	/// <inheritdoc />
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		if (!options.Value.RegisterBotCommands)
		{
			logger.LogInformation("SetMyCommands skipped (RegisterBotCommands=false).");
			return;
		}

		var catalogList = catalogs as IList<IBotCommandCatalog> ?? [.. catalogs];
		if (catalogList.Count == 0)
		{
			// Client-only wiring (no router): leave whatever Telegram already has alone.
			logger.LogDebug("SetMyCommands skipped (no IBotCommandCatalog registered).");
			return;
		}

		var descriptors = catalogList
			.SelectMany(static c => c.Commands)
			.GroupBy(static c => c.Name, StringComparer.OrdinalIgnoreCase)
			.Select(static g => g.First())
			.OrderBy(static c => c.Name, StringComparer.Ordinal)
			.ToArray();

		var payload = BotCommandRegistration.BuildPayload(descriptors);
		if (payload.Count == 0)
		{
			// An empty setMyCommands payload deletes the menu; leave whatever Telegram already has alone.
			logger.LogDebug("SetMyCommands skipped (catalog contributed no commands).");
			return;
		}

		try
		{
			await botClient.SetMyCommands(payload, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			// Command metadata is cosmetic: never turn a Telegram-side failure into a failed host start.
			logger.LogError(ex, "Failed to register {Count} bot command(s) with Telegram (setMyCommands).", payload.Count);
			return;
		}

		logger.LogInformation("Registered {Count} bot command(s) with Telegram (setMyCommands).", payload.Count);
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
