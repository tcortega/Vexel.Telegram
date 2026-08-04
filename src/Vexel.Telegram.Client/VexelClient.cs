using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Vexel.Telegram.Client;

/// <summary>
/// Host-agnostic Telegram bot client shell.
/// Receive loop and per-chat dispatch land in later v2 slices.
/// </summary>
/// <param name="logger">The logger for this class.</param>
/// <param name="botClient">The Telegram Bot API client.</param>
/// <param name="options">The configured options for this client.</param>
public sealed class VexelClient(
	ILogger<VexelClient> logger,
	ITelegramBotClient botClient,
	IOptions<VexelClientOptions> options)
{
	private readonly VexelClientOptions _options = options.Value;

	/// <summary>
	/// Runs the client until <paramref name="stoppingToken"/> is cancelled.
	/// </summary>
	/// <param name="stoppingToken">Token that signals the client should stop.</param>
	/// <returns>A task that completes when the client stops.</returns>
	public Task RunAsync(CancellationToken stoppingToken)
	{
		_ = botClient;
		_ = _options;
		logger.LogInformation("VexelClient shell started; receive loop is not implemented yet.");
		return Task.Delay(Timeout.Infinite, stoppingToken);
	}
}
