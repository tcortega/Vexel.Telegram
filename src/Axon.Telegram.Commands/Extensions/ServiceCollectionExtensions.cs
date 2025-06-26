using Axon.Telegram.Commands.Responders;
using Axon.Telegram.Commands.Services;
using Axon.Telegram.Commands.Services.Execution;
using Axon.Telegram.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Remora.Commands.Extensions;

namespace Axon.Telegram.Commands.Extensions;

/// <summary>
/// Provides extension methods for setting up command handling.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds and configured Telegram command handling services.
    /// This is the primary entry point for setting up the command system.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional action to configure command options.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddTelegramCommandHandling(this IServiceCollection services,
        Action<CommandOptions>? configure = null)
    {
        services.AddCommands();

        services.Configure(configure ?? (_ => { }));

        services.AddResponder<CommandResponder>();

        services.TryAddScoped<ContextInjectionService>();

        services.TryAddSingleton<ExecutionEventCollectorService>();

        services.TryAddScoped<ICommandContext>
        (sp =>
            {
                var injector = sp.GetRequiredService<ContextInjectionService>();
                return injector.Context
                       ?? throw new InvalidOperationException
                       (
                           "The command context was not available from the injection service. " +
                           "This indicates a framework bug or that the service was resolved outside of a command execution scope."
                       );
            }
        );

        services.AddHostedService<CommandRegistrationService>();

        return services;
    }

    /// <summary>
    /// Adds a post-execution event handler to the service collection.
    /// This allows developers to easily extend the framework with custom logic
    /// that runs after a command is executed.
    /// </summary>
    /// <param name="serviceCollection">The service collection.</param>
    /// <typeparam name="TEvent">The type of the event handler, which must implement IPostExecutionEvent.</typeparam>
    /// <returns>The service collection, with the event handler added.</returns>
    public static IServiceCollection AddPostExecutionEvent<TEvent>(this IServiceCollection serviceCollection)
        where TEvent : class, IPostExecutionEvent
    {
        serviceCollection.AddScoped<IPostExecutionEvent, TEvent>();
        return serviceCollection;
    }
}