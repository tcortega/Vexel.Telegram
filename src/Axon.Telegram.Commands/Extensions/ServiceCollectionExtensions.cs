using Axon.Telegram.Abstractions.Responders;
using Axon.Telegram.Client.Extensions;
using Axon.Telegram.Commands.Contexts;
using Axon.Telegram.Commands.Execution;
using Axon.Telegram.Commands.Prefix;
using Axon.Telegram.Commands.Registration;
using Axon.Telegram.Commands.Responders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Remora.Commands.Extensions;
using Remora.Commands.Tokenization;
using Remora.Commands.Trees;
using Remora.Extensions.Options.Immutable;

namespace Axon.Telegram.Commands.Extensions;

/// <summary>
/// Defines extension methods for the <see cref="IServiceCollection"/> interface.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all services required for Telegram-integrated commands.
    /// </summary>
    /// <param name="serviceCollection">The service collection.</param>
    /// <param name="useDefaultCommandResponder">Whether to add a default command responder.</param>
    /// <returns>The service collection, with Telegram commands.</returns>
    public static IServiceCollection AddTelegramCommands
    (
        this IServiceCollection serviceCollection,
        bool useDefaultCommandResponder = true
    )
    {
        serviceCollection
            .TryAddScoped<ContextInjectionService>();

        // Set up context injection
        serviceCollection
            .TryAddTransient
            (s =>
                {
                    var injectionService = s.GetRequiredService<ContextInjectionService>();
                    return injectionService.Context ?? throw new InvalidOperationException
                    (
                        "No operation context has been set for this scope."
                    );
                }
            );

        serviceCollection
            .TryAddTransient
            (s =>
                {
                    var injectionService = s.GetRequiredService<ContextInjectionService>();
                    return injectionService.Context as ICommandContext ?? throw new InvalidOperationException
                    (
                        "No command context has been set for this scope."
                    );
                }
            );

        serviceCollection
            .TryAddTransient
            (s =>
                {
                    var injectionService = s.GetRequiredService<ContextInjectionService>();
                    return injectionService.Context as IMessageContext ?? throw new InvalidOperationException
                    (
                        "No message context has been set for this scope."
                    );
                }
            );

        serviceCollection
            .TryAddTransient
            (s =>
                {
                    var injectionService = s.GetRequiredService<ContextInjectionService>();
                    return injectionService.Context as ITextCommandContext ?? throw new InvalidOperationException
                    (
                        "No text command context has been set for this scope."
                    );
                }
            );

        // Configure option types
        serviceCollection.Configure<TokenizerOptions>(opt => opt);
        serviceCollection.Configure<TreeSearchOptions>
        (opt => opt with { KeyComparison = StringComparison.OrdinalIgnoreCase }
        );

        serviceCollection.AddCommands();

        // Add the default prefix matcher if the end user hasn't already registered one
        serviceCollection.TryAddTransient<ICommandPrefixMatcher, SimplePrefixMatcher>();

        if (useDefaultCommandResponder)
        {
            serviceCollection.AddCommandResponder();
        }

        serviceCollection.TryAddSingleton<ExecutionEventCollectorService>();

        // Add the command registrar service
        serviceCollection.TryAddTransient<CommandRegistrar>();

        return serviceCollection;
    }

    /// <summary>
    /// Adds the command responder to the system.
    /// </summary>
    /// <param name="serviceCollection">The service collection.</param>
    /// <param name="optionsConfigurator">The option configurator.</param>
    /// <returns>The collection, with the command responder.</returns>
    public static IServiceCollection AddCommandResponder
    (
        this IServiceCollection serviceCollection,
        Action<CommandResponderOptions>? optionsConfigurator = null
    )
    {
        optionsConfigurator ??= _ => { };

        serviceCollection.AddResponder<CommandResponder>();
        serviceCollection.Configure(optionsConfigurator);

        return serviceCollection;
    }

    /// <summary>
    /// Adds a preparation error event to the service collection.
    /// </summary>
    /// <param name="serviceCollection">The service collection.</param>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <returns>The collection, with the event.</returns>
    public static IServiceCollection AddPreparationErrorEvent<TEvent>
    (
        this IServiceCollection serviceCollection
    )
        where TEvent : class, IPreparationErrorEvent
    {
        serviceCollection.AddScoped<IPreparationErrorEvent, TEvent>();
        return serviceCollection;
    }

    /// <summary>
    /// Adds a pre-execution event to the service collection.
    /// </summary>
    /// <param name="serviceCollection">The service collection.</param>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <returns>The collection, with the event.</returns>
    public static IServiceCollection AddPreExecutionEvent<TEvent>
    (
        this IServiceCollection serviceCollection
    )
        where TEvent : class, IPreExecutionEvent
    {
        serviceCollection.AddScoped<IPreExecutionEvent, TEvent>();
        return serviceCollection;
    }

    /// <summary>
    /// Adds a post-execution event to the service collection.
    /// </summary>
    /// <param name="serviceCollection">The service collection.</param>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <returns>The collection, with the event.</returns>
    public static IServiceCollection AddPostExecutionEvent<TEvent>
    (
        this IServiceCollection serviceCollection
    )
        where TEvent : class, IPostExecutionEvent
    {
        serviceCollection.AddScoped<IPostExecutionEvent, TEvent>();
        return serviceCollection;
    }

    /// <summary>
    /// Adds a pre- and post-execution event to the service collection.
    /// </summary>
    /// <param name="serviceCollection">The service collection.</param>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <returns>The collection, with the event.</returns>
    public static IServiceCollection AddExecutionEvent<TEvent>
    (
        this IServiceCollection serviceCollection
    )
        where TEvent : class, IPreExecutionEvent, IPostExecutionEvent
    {
        serviceCollection
            .AddPreExecutionEvent<TEvent>()
            .AddPostExecutionEvent<TEvent>();

        return serviceCollection;
    }
}