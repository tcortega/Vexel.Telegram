using Axon.Telegram.Polling;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Axon.Telegram.Hosting;

/// <summary>
/// An <see cref="IHostedService"/> to manage the <see cref="TelegramPollingClient"/>.
/// </summary>
internal sealed class TelegramBotService(ILogger<TelegramBotService> logger, TelegramPollingClient pollingClient)
    : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting Telegram Bot Service.");
        // Do not await this, as it will block the application from starting.
        // It's a long-running task that will be cancelled by the host.
        _ = pollingClient.RunAsync(cancellationToken).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                logger.LogCritical(t.Exception, "The Telegram Polling Client has terminated unexpectedly.");
            }
            else
            {
                logger.LogInformation("Telegram Polling Client has shut down gracefully.");
            }
        }, CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping Telegram Bot Service.");
        // The cancellation token passed to RunAsync will be cancelled by the host,
        // which will gracefully stop the polling client.
        return Task.CompletedTask;
    }
}