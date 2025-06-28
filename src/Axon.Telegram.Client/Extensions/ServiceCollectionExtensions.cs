using Axon.Telegram.Abstractions.Responders;
using Axon.Telegram.Client.Responders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Remora.Extensions.Options.Immutable;
using Telegram.Bot;

namespace Axon.Telegram.Client.Extensions;

/// <summary>
/// Defines extension methods for the <see cref="IServiceCollection"/> class.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds services required by the Axon Telegram client system.
    /// </summary>
    /// <param name="serviceCollection">The service collection.</param>
    /// <param name="tokenFactory">A factory function to retrieve the bot token.</param>
    ///     /// <param name="configureOptions">A delegate to configure the client options.</param>
    /// <returns>The service collection, with the services added.</returns>
    public static IServiceCollection AddAxonTelegramClient
    (
        this IServiceCollection serviceCollection,
        Func<IServiceProvider, string> tokenFactory,
        Action<AxonClientOptions>? configureOptions = null
    )
    {
        configureOptions ??= _ => { };
        serviceCollection.Configure(configureOptions);

        serviceCollection.TryAddSingleton<IResponderDispatchService, ResponderDispatchService>();
        serviceCollection.TryAddSingleton<IResponderTypeRepository>
        (s => s.GetRequiredService<IOptions<ResponderService>>().Value
        );

        serviceCollection.TryAddSingleton<AxonClient>();
        serviceCollection.Configure<ResponderDispatchOptions>(() => new());

        serviceCollection.TryAddSingleton<ITelegramBotClient>(sp => new TelegramBotClient(tokenFactory(sp)));

        return serviceCollection;
    }

    /// <summary>
    /// Adds a responder to the service collection. This method registers the responder as being available for all
    /// <see cref="IResponder{T}"/> implementations it supports.
    /// </summary>
    /// <param name="serviceCollection">The service collection.</param>
    /// <param name="group">The group the responder belongs to.</param>
    /// <typeparam name="TResponder">The concrete responder type.</typeparam>
    /// <returns>The service collection, with the responder added.</returns>
    public static IServiceCollection AddResponder<TResponder>
    (
        this IServiceCollection serviceCollection,
        ResponderGroup group = ResponderGroup.Normal
    )
        where TResponder : IResponder
    {
        return serviceCollection.AddResponder(typeof(TResponder), group);
    }

    /// <summary>
    /// Adds a responder to the service collection. This method registers the responder as being available for all
    /// <see cref="IResponder{T}"/> implementations it supports.
    /// </summary>
    /// <param name="serviceCollection">The service collection.</param>
    /// <param name="responderType">The type implementing <see cref="IResponder"/>.</param>
    /// <param name="group">The group the responder belongs to.</param>
    /// <returns>The service collection, with the responder added.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown if responderType does not implement <see cref="IResponder"/>.
    /// </exception>
    public static IServiceCollection AddResponder
    (
        this IServiceCollection serviceCollection,
        Type responderType,
        ResponderGroup group = ResponderGroup.Normal
    )
    {
        if (!responderType.IsResponder())
        {
            throw new ArgumentException
            (
                $"{nameof(responderType)} must implement {nameof(IResponder)}.",
                nameof(responderType)
            );
        }

        var responderTypeInterfaces = responderType.GetInterfaces();
        var responderInterfaces = responderTypeInterfaces.Where
        (r => r.IsGenericType && r.GetGenericTypeDefinition() == typeof(IResponder<>)
        );

        foreach (var responderInterface in responderInterfaces)
        {
            serviceCollection.AddScoped(responderInterface, responderType);
        }

        serviceCollection.AddScoped(responderType);

        serviceCollection.Configure<ResponderService>
        (responderService => responderService.RegisterResponderType(responderType, group)
        );

        return serviceCollection;
    }
}