using Axon.Telegram.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Axon.Telegram.Hosting;

/// <summary>
/// The <see cref="IHostedService"/> that will run telegram-related processes in the background.
/// </summary>
public sealed class AxonService(AxonClient client, ILogger<AxonService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await client.RunAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while running the Axon Telegram client.");
        }
    }
}