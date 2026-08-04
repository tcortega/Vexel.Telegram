using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vexel.Telegram.Client;

namespace Vexel.Telegram.Hosting;

/// <summary>
/// Background service that runs <see cref="VexelClient"/>.
/// </summary>
public sealed class VexelService(VexelClient client, ILogger<VexelService> logger) : BackgroundService
{
	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			await client.RunAsync(stoppingToken).ConfigureAwait(false);
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
