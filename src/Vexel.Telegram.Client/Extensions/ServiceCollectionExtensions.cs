using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;
using Vexel.Telegram.Client.Dispatch;

namespace Vexel.Telegram.Client.Extensions;

/// <summary>
/// DI registration helpers for the Vexel Telegram client.
/// </summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Adds the Vexel Telegram client, update scheduler, and a configured <see cref="ITelegramBotClient"/>.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="tokenFactory">Factory that returns the bot token.</param>
	/// <param name="configureOptions">Optional client options configuration.</param>
	/// <returns>The same service collection.</returns>
	public static IServiceCollection AddVexelTelegramClient(
		this IServiceCollection services,
		Func<IServiceProvider, string> tokenFactory,
		Action<VexelClientOptions>? configureOptions = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(tokenFactory);

		_ = services.Configure(configureOptions ?? (static _ => { }));

		_ = GetOrAddRawHandlerRegistry(services);
		services.TryAddSingleton<ITelegramBotClient>(sp => new TelegramBotClient(tokenFactory(sp)));
		services.TryAddSingleton<IUpdateDispatcher, UpdateDispatcher>();
		services.TryAddSingleton<UpdateScheduler>();
		services.TryAddSingleton<VexelClient>();

		return services;
	}

	/// <summary>
	/// Adds a raw update handler that is resolved independently of every other raw handler, so a
	/// construction or dependency failure in one handler cannot stop the others from running.
	/// </summary>
	/// <typeparam name="THandler">The handler implementation type.</typeparam>
	/// <param name="services">The service collection.</param>
	/// <param name="lifetime">Handler lifetime; scoped by default, one instance per update.</param>
	/// <returns>The same service collection.</returns>
	public static IServiceCollection AddRawUpdateHandler<THandler>(
		this IServiceCollection services,
		ServiceLifetime lifetime = ServiceLifetime.Scoped)
		where THandler : class, IRawUpdateHandler
	{
		ArgumentNullException.ThrowIfNull(services);

		services.TryAdd(new ServiceDescriptor(typeof(THandler), typeof(THandler), lifetime));
		GetOrAddRawHandlerRegistry(services).Add(typeof(THandler));

		return services;
	}

	private static RawUpdateHandlerRegistry GetOrAddRawHandlerRegistry(IServiceCollection services)
	{
		foreach (var descriptor in services)
		{
			if (descriptor.ServiceType == typeof(RawUpdateHandlerRegistry)
				&& descriptor.ImplementationInstance is RawUpdateHandlerRegistry existing)
			{
				return existing;
			}
		}

		var registry = new RawUpdateHandlerRegistry();
		_ = services.AddSingleton(registry);

		return registry;
	}
}
