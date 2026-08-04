using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Vexel.Telegram.Client.Dispatch;

namespace Vexel.Telegram.Client;

/// <summary>
/// Host-agnostic Telegram bot client: long-polling or webhook receive plus per-chat ordered dispatch.
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
	/// Runs the receive loop until <paramref name="stoppingToken"/> is cancelled, then
	/// drains buffered updates without a shutdown budget.
	/// </summary>
	/// <param name="stoppingToken">Token that signals the client should stop.</param>
	/// <returns>A task that completes when the client stops.</returns>
	public Task RunAsync(CancellationToken stoppingToken) =>
		RunAsync(stoppingToken, CancellationToken.None);

	/// <summary>
	/// Runs the configured receive mode until <paramref name="stoppingToken"/> is cancelled.
	/// </summary>
	/// <param name="stoppingToken">Token that signals the client should stop.</param>
	/// <param name="drainToken">
	/// Bounds the post-stop drain of buffered updates to the caller's shutdown budget, so the
	/// drain cannot outlive the container that its handlers resolve from.
	/// </param>
	/// <returns>A task that completes when the client stops.</returns>
	public async Task RunAsync(CancellationToken stoppingToken, CancellationToken drainToken)
	{
		// Fail fast before touching the network when polling and webhook are both (or neither) set.
		_options.ValidateReceiveMode();

		try
		{
			if (_options.ReceiveMode == TelegramReceiveMode.Webhook)
			{
				await RunWebhookAsync(stoppingToken).ConfigureAwait(false);
			}
			else
			{
				await RunPollingAsync(stoppingToken).ConfigureAwait(false);
			}
		}
		finally
		{
			// Drain here, while the container and every handler dependency are still alive.
			// Disposal of the scheduler singleton itself stays with the container.
			await scheduler.StopAsync(drainToken).ConfigureAwait(false);
		}

		logger.LogInformation("VexelClient stopped");
	}

	private async Task RunPollingAsync(CancellationToken stoppingToken)
	{
		try
		{
			// A leftover webhook blocks getUpdates; clear it so polling always owns receive.
			await botClient
				.DeleteWebhook(dropPendingUpdates: _options.DropPendingUpdates, cancellationToken: stoppingToken)
				.ConfigureAwait(false);
		}
		catch (Exception ex) when (IsShutdownCancellation(ex, stoppingToken))
		{
			// Stopped before receive ever started.
			return;
		}

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
	}

	private async Task RunWebhookAsync(CancellationToken stoppingToken)
	{
		var webhook = _options.Webhook!;

		logger.LogInformation(
			"VexelClient starting webhook mode (Url={Url}, Path={Path}, DropPendingUpdates={DropPendingUpdates}, LaneCapacity={LaneCapacity}). "
			+ "Map the endpoint explicitly (MapTelegramWebhook); nothing is auto-mapped.",
			webhook.Url,
			webhook.Path,
			_options.DropPendingUpdates,
			_options.LaneCapacity);

		if (!WebhookPathsMatch(webhook.Url!.AbsolutePath, webhook.Path))
		{
			// Not fatal: a reverse proxy or ingress may legitimately rewrite the public path.
			logger.LogWarning(
				"Webhook Url path '{UrlPath}' differs from the mapped endpoint path '{Path}'. "
				+ "Telegram POSTs to the Url exactly, so every delivery 404s unless something in "
				+ "front of the app rewrites the path.",
				webhook.Url.AbsolutePath,
				webhook.Path);
		}

		try
		{
			await botClient.SetWebhook(
				url: webhook.Url!.AbsoluteUri,
				dropPendingUpdates: _options.DropPendingUpdates,
				secretToken: webhook.SecretToken,
				cancellationToken: stoppingToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (IsShutdownCancellation(ex, stoppingToken))
		{
			// Stopped before the registration finished; there is nothing left to keep alive.
			return;
		}

		try
		{
			// Ingress is the explicitly mapped HTTP endpoint; this task just keeps the host alive
			// and owns the scheduler drain on stop.
			await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			// Normal shutdown.
		}
	}

	/// <summary>
	/// True when <paramref name="exception"/> is a Bot API call that lost a race with shutdown.
	/// Telegram.Bot wraps a cancelled request in <c>RequestException</c>, so without unwrapping, a
	/// stop landing mid-call reads as a fatal receive error and takes the host down noisily.
	/// </summary>
	internal static bool IsShutdownCancellation(Exception exception, CancellationToken stoppingToken)
	{
		if (!stoppingToken.IsCancellationRequested)
		{
			return false;
		}

		for (var current = exception; current is not null; current = current.InnerException)
		{
			if (current is OperationCanceledException)
			{
				return true;
			}
		}

		return false;
	}

	// Routing matches case-insensitively and ignores surrounding slashes, so neither is a mismatch.
	internal static bool WebhookPathsMatch(string urlPath, string mappedPath) =>
		string.Equals(urlPath.Trim('/'), mappedPath.Trim('/'), StringComparison.OrdinalIgnoreCase);

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
