using Axon.Telegram.Client;
using Axon.Telegram.Client.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Axon.Telegram.Hosting.Extensions;

/// <summary>
/// Defines extension methods for the <see cref="IHostBuilder"/> interface.
/// </summary>
public static class HostBuilderExtensions
{
    /// <summary>
    /// Adds the required services for Axon Telegram and a <see cref="IHostedService"/> implementation to an
    /// <see cref="IHostBuilder"/>.
    /// </summary>
    /// <param name="hostBuilder">The host builder.</param>
    /// <param name="tokenFactory">A factory function to retrieve the bot token.</param>
    /// <param name="configureClientOptions">A function that retrieves the configured <see cref="AxonClientOptions"/>.</param>
    /// <returns>The host builder, with the services added.</returns>
    public static IHostBuilder AddTelegramService
    (
        this IHostBuilder hostBuilder,
        Func<IServiceProvider, string> tokenFactory,
        Action<AxonClientOptions>? configureClientOptions = null
    )
    {
        hostBuilder.ConfigureServices((_, serviceCollection) =>
            serviceCollection.AddTelegramService(tokenFactory, configureClientOptions));

        return hostBuilder;
    }

    /// <summary>
    /// Adds the required services for Axon Telegram and a <see cref="IHostedService"/> implementation to an
    /// <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="serviceCollection">The service collection.</param>
    /// <param name="tokenFactory">A factory function to retrieve the bot token.</param>
    /// <param name="configureClientOptions">A function that retrieves the configured <see cref="AxonClientOptions"/>.</param>
    /// <returns>The service collection, with the services added.</returns>
    public static IServiceCollection AddTelegramService
    (
        this IServiceCollection serviceCollection,
        Func<IServiceProvider, string> tokenFactory,
        Action<AxonClientOptions>? configureClientOptions = null
    )
    {
        serviceCollection.AddAxonTelegramClient(tokenFactory, configureClientOptions);
        serviceCollection.TryAddSingleton<AxonService>();

        serviceCollection
            .AddSingleton<IHostedService, AxonService>(serviceProvider =>
                serviceProvider.GetRequiredService<AxonService>());

        return serviceCollection;
    }
}