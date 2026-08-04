using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vexel.Telegram.Client;

namespace Vexel.Telegram.Hosting;

/// <summary>
/// Background service that runs <see cref="VexelClient"/>.
/// </summary>
public sealed class VexelService(VexelClient client, ILogger<VexelService> logger) : BackgroundService
{
	private readonly CancellationTokenSource _shutdownBudgetCts = new();

	/// <inheritdoc />
	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		// The host's shutdown budget bounds the drain that RunAsync performs on its way out.
		using var registration = cancellationToken.Register(
			static state =>
			{
				try
				{
					((CancellationTokenSource)state!).Cancel();
				}
				catch (ObjectDisposedException)
				{
				}
			},
			_shutdownBudgetCts);

		await base.StopAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public override void Dispose()
	{
		_shutdownBudgetCts.Dispose();
		base.Dispose();
	}

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			await client.RunAsync(stoppingToken, _shutdownBudgetCts.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			// normal shutdown
		}
		catch (Exception ex)
		{
			// Fail-fast polish lands in T9; shell logs for now so the host can still stop cleanly.
			logger.LogError(ex, "An error occurred while running the Vexel Telegram client.");
		}
	}
}
