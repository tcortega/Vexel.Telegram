using Vexel.Telegram.Client.Extensions;
using Vexel.Telegram.Commands.Contexts;
using Vexel.Telegram.Interactivity.Contexts;
using Vexel.Telegram.Interactivity.Responders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Remora.Commands.Extensions;

namespace Vexel.Telegram.Interactivity.Extensions;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Adds Telegram interactivity services to the service collection.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <returns>The service collection, for chaining.</returns>
	public static IServiceCollection AddTelegramInteractivity(this IServiceCollection services)
	{
		_ = services.AddMemoryCache();
		_ = services.AddResponder<InteractivityResponder>();

		services
			.TryAddTransient
			(s =>
				{
					var injectionService = s.GetRequiredService<ContextInjectionService>();
					return injectionService.Context as IInteractionContext ?? throw new InvalidOperationException
					(
						"No interaction context has been set for this scope."
					);
				}
			);

		services
			.TryAddTransient
			(s =>
				{
					var injectionService = s.GetRequiredService<ContextInjectionService>();
					return injectionService.Context as IInteractionCommandContext ?? throw new InvalidOperationException
					(
						"No interaction command context has been set for this scope."
					);
				}
			);

		return services;
	}

	/// <summary>
	/// Adds an interactive command group to the service collection.
	/// </summary>
	/// <param name="serviceCollection">The service collection.</param>
	/// <typeparam name="TInteractionGroup">The entity type.</typeparam>
	/// <returns>The collection, with the entity added.</returns>
	public static IServiceCollection AddInteractionGroup<TInteractionGroup>
	(
		this IServiceCollection serviceCollection
	)
		where TInteractionGroup : InteractionGroup
		=> serviceCollection.AddInteractiveEntity(typeof(TInteractionGroup));

	/// <summary>
	/// Adds an interactive entity to the service collection.
	/// </summary>
	/// <param name="serviceCollection">The service collection.</param>
	/// <param name="entityType">The entity type.</param>
	/// <returns>The collection, with the entity added.</returns>
	public static IServiceCollection AddInteractiveEntity
	(
		this IServiceCollection serviceCollection,
		Type entityType
	)
	{
		_ = serviceCollection.AddCommandTree(Constants.InteractionTree)
			.WithCommandGroup(entityType);

		return serviceCollection;
	}
}
