using Vexel.Telegram.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Vexel.Telegram.Hosting;

/// <summary>
/// The <see cref="IHostedService"/> that will run telegram-related processes in the background.
/// </summary>
public sealed class VexelService(VexelClient client, ILogger<VexelService> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			await client.RunAsync(stoppingToken);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "An error occurred while running the Vexel Telegram client.");
		}
	}
}
