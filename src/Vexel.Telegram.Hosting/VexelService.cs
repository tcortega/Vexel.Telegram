using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vexel.Telegram.Client;

namespace Vexel.Telegram.Hosting;

/// <summary>
/// Background service that runs <see cref="VexelClient"/>.
/// Fatal receive-loop failures rethrow so the host stops (no zombie process).
/// </summary>
public sealed class VexelService(VexelClient client, ILogger<VexelService> logger) : BackgroundService
{
	private readonly CancellationTokenSource _shutdownBudgetCts = new();

	/// <inheritdoc />
	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		try
		{
			await base.StopAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			// Once the host stops waiting its shutdown budget is spent, either because the client
			// already finished or because the budget expired; wind any remaining drain down.
			CancelShutdownBudget();
		}
	}

	/// <inheritdoc />
	public override void Dispose()
	{
		CancelShutdownBudget();
		_shutdownBudgetCts.Dispose();
		base.Dispose();
	}

	private void CancelShutdownBudget()
	{
		try
		{
			_shutdownBudgetCts.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
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
			// Fail-fast: rethrow so BackgroundServiceExceptionBehavior.StopHost tears the process
			// down instead of leaving a zombie host with a dead bot.
			logger.LogCritical(ex, "Fatal error while running the Vexel Telegram client; stopping host.");
			throw;
		}
	}
}
