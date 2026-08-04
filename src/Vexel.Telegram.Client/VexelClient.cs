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
	UpdateScheduler scheduler) : IAsyncDisposable
{
	private readonly VexelClientOptions _options = options.Value;
	private int _disposeState;

	/// <summary>
	/// Runs the polling receive loop until <paramref name="stoppingToken"/> is cancelled.
	/// </summary>
	/// <param name="stoppingToken">Token that signals the client should stop.</param>
	/// <returns>A task that completes when the client stops.</returns>
	public async Task RunAsync(CancellationToken stoppingToken)
	{
		var receiverOptions = new ReceiverOptions
		{
			DropPendingUpdates = _options.DropPendingUpdates,
			// Non-null AllowedUpdates is required when DropPendingUpdates is true;
			// an empty list means "all update kinds" in Telegram.Bot.
			AllowedUpdates = [],
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
			await scheduler.DisposeAsync().ConfigureAwait(false);
		}

		logger.LogInformation("VexelClient stopped");
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposeState, 1) != 0)
		{
			return;
		}

		await scheduler.DisposeAsync().ConfigureAwait(false);
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

	private Task HandlePollingErrorAsync(ITelegramBotClient _, Exception exception, CancellationToken cancellationToken)
	{
		if (cancellationToken.IsCancellationRequested
			&& exception is OperationCanceledException)
		{
			return Task.CompletedTask;
		}

		logger.LogError(exception, "Telegram polling error");
		return Task.CompletedTask;
	}
}
