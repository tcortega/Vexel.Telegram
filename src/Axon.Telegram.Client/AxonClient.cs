using Axon.Telegram.Abstractions.Responders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace Axon.Telegram.Client;

/// <summary>
/// The main client for interacting with the Telegram Bot API. This class manages the bot's lifecycle,
/// receives updates, and dispatches them to registered responders.
/// </summary>
/// <param name="logger">The logger for this class.</param>
/// <param name="botClient">The Telegram Bot API client.</param>
/// <param name="responderDispatchService">The service responsible for dispatching updates to responders.</param>
/// <param name="options">The configured options for this client.</param>
/// <remarks>
/// This class is host-agnostic. It is intended to be instantiated and run by a host,
/// such as the <c>AxonHostedService</c> in the Axon.Telegram.Hosting package.
/// </remarks>
public class AxonClient(
	ILogger<AxonClient> logger,
	ITelegramBotClient botClient,
	IResponderDispatchService responderDispatchService,
	IOptions<AxonClientOptions> options)
{
	private readonly AxonClientOptions _options = options.Value;

	/// <summary>
	/// Runs the client, connecting to Telegram and beginning the process of receiving and dispatching updates.
	/// </summary>
	/// <remarks>
	/// This method will run indefinitely until the cancellation token is triggered.
	/// It currently uses long polling to receive updates.
	/// </remarks>
	/// <param name="stoppingToken">A token to signal that the client should stop its operations.</param>
	/// <returns>A <see cref="Task"/> that represents the long running-polling operation.</returns>
	public async Task RunAsync(CancellationToken stoppingToken)
	{
		var me = await botClient.GetMe(stoppingToken);
		logger.LogInformation("Successfully connected to Telegram. Bot: {Username} (ID: {BotId})", me.Username, me.Id);

		await botClient.ReceiveAsync
		(
			updateHandler: (_, update, ct) => responderDispatchService.DispatchAsync(update, ct),
			errorHandler: HandlePollingErrorAsync,
			receiverOptions: _options.ReceiverOptions,
			cancellationToken: stoppingToken
		);

		logger.LogInformation("Telegram client has stopped receiving updates.");
	}

	private Task HandlePollingErrorAsync(ITelegramBotClient _, Exception exception,
		CancellationToken cancellationToken)
	{
		var errorMessage = exception switch
		{
			ApiRequestException apiRequestException =>
				$"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
			_ => exception.ToString(),
		};

		logger.LogError(exception, "A polling error occurred: {ErrorMessage}", errorMessage);
		return Task.CompletedTask;
	}
}
