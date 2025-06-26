using Axon.Telegram.Hosting;
using Axon.Telegram.Polling;
using Axon.Telegram.Responders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;

namespace Axon.Telegram.Extensions;

/// <summary>
/// Provides extension methods for setting up the Telegram bot framework.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds and configures the Remora.Telegram services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="tokenFactory">A factory function to retrieve the bot token.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddTelegramBot
    (
        this IServiceCollection services,
        Func<IServiceProvider, string> tokenFactory
    )
    {
        services.TryAddSingleton<ITelegramBotClient>(sp => new TelegramBotClient(tokenFactory(sp)));
        services.TryAddSingleton<UpdateDispatchService>();
        services.TryAddSingleton<TelegramPollingClient>();

        // This allows the PollingClient to be run as a background service.
        services.AddHostedService<TelegramBotService>();

        // // Add the base for the command services
        // services.AddCommandServices();

        return services;
    }

    /// <summary>
    /// Adds a responder for a given event type.
    /// </summary>
    /// <typeparam name="TResponder">The type of the responder.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddResponder<TResponder>(this IServiceCollection services)
        where TResponder : class
    {
        var responderInterfaces = typeof(TResponder)
            .GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IResponder<>));

        services.TryAddScoped<TResponder>();

        foreach (var responderInterface in responderInterfaces)
        {
            services.AddScoped(responderInterface, sp => sp.GetRequiredService<TResponder>());
        }

        return services;
    }
}