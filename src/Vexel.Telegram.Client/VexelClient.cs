using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Vexel.Telegram.Client.Dispatch;

namespace Vexel.Telegram.Client;

/// <summary>
/// Host-agnostic Telegram bot client: long-polling receive loop plus per-chat ordered dispatch.
/// </summary>
public sealed class VexelClient(
	ILogger<VexelClient> logger,
	ITelegramBotClient botClient,
	IOptions<VexelClientOptions> options,
	UpdateScheduler scheduler)
{
	private static readonly TimeSpan s_pollingErrorCooldown = TimeSpan.FromSeconds(2);

	private readonly VexelClientOptions _options = options.Value;

	/// <summary>
	/// Runs the polling receive loop until <paramref name="stoppingToken"/> is cancelled, then
	/// drains buffered updates without a shutdown budget.
	/// </summary>
	/// <param name="stoppingToken">Token that signals the client should stop.</param>
	/// <returns>A task that completes when the client stops.</returns>
	public Task RunAsync(CancellationToken stoppingToken) =>
		RunAsync(stoppingToken, CancellationToken.None);

	/// <summary>
	/// Runs the polling receive loop until <paramref name="stoppingToken"/> is cancelled.
	/// </summary>
	/// <param name="stoppingToken">Token that signals the client should stop.</param>
	/// <param name="drainToken">
	/// Bounds the post-stop drain of buffered updates to the caller's shutdown budget, so the
	/// drain cannot outlive the container that its handlers resolve from.
	/// </param>
	/// <returns>A task that completes when the client stops.</returns>
	public async Task RunAsync(CancellationToken stoppingToken, CancellationToken drainToken)
	{
		// AllowedUpdates stays null: Telegram.Bot then receives every update kind, whereas an
		// explicit empty list excludes reactions and chat member updates.
		var receiverOptions = new ReceiverOptions
		{
			DropPendingUpdates = _options.DropPendingUpdates,
		};

		logger.LogInformation(
			"VexelClient starting polling (DropPendingUpdates={DropPendingUpdates}, LaneCapacity={LaneCapacity})",
			_options.DropPendingUpdates,
			_options.LaneCapacity);

		try
		{
			await botClient.ReceiveAsync(
				updateHandler: HandleUpdateAsync,
				errorHandler: HandlePollingErrorAsync,
				receiverOptions: receiverOptions,
				cancellationToken: stoppingToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			// Normal shutdown.
		}
		finally
		{
			// Drain here, while the container and every handler dependency are still alive.
			// Disposal of the scheduler singleton itself stays with the container.
			await scheduler.StopAsync(drainToken).ConfigureAwait(false);
		}

		logger.LogInformation("VexelClient stopped");
	}

	private async Task HandleUpdateAsync(ITelegramBotClient _, Update update, CancellationToken cancellationToken)
	{
		try
		{
			// Await enqueue (backpressure) only; processing runs on the lane worker.
			await scheduler.ScheduleAsync(update, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Shutting down while applying backpressure.
		}
		catch (Exception ex)
		{
			// Never let a single update tear down the polling loop.
			logger.LogError(ex, "Failed to schedule update {UpdateId}", update.Id);
		}
	}

	private async Task HandlePollingErrorAsync(ITelegramBotClient _, Exception exception, CancellationToken cancellationToken)
	{
		if (cancellationToken.IsCancellationRequested
			&& exception is OperationCanceledException)
		{
			return;
		}

		logger.LogError(exception, "Telegram polling error");

		// The receiver re-enters GetUpdates as soon as this returns; cool down so an unreachable
		// API cannot spin the loop at full speed.
		try
		{
			await Task.Delay(s_pollingErrorCooldown, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			// Shutting down during the cooldown.
		}
	}
}
