using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Vexel.Telegram.Client.Dispatch;
using Vexel.Telegram.Client.Webhook;

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
		services.TryAddSingleton(static sp => new UpdateDispatcher(
			sp.GetRequiredService<IServiceScopeFactory>(),
			sp.GetRequiredService<RawUpdateHandlerRegistry>(),
			sp.GetServices<IUpdateCompletionHook>(),
			sp.GetRequiredService<IOptions<VexelClientOptions>>(),
			sp.GetRequiredService<ILogger<UpdateDispatcher>>(),
			ResolveRouter(sp)));
		services.TryAddSingleton<UpdateScheduler>();
		services.TryAddSingleton<WebhookUpdateReceiver>();
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

	private static IUpdateRouter? ResolveRouter(IServiceProvider provider)
	{
		IUpdateRouter[] routers = [.. provider.GetServices<IUpdateRouter>()];

		return routers.Length switch
		{
			0 => null,
			1 => routers[0],
			_ => throw new InvalidOperationException(
				"Multiple IUpdateRouter services are registered ("
				+ string.Join(", ", routers.Select(static r => r.GetType().FullName))
				+ "). Vexel dispatches through a single router: an app-registered IUpdateRouter replaces "
				+ "TelegramRouter and silently disables every [Command], [Callback], [InlineQuery], "
				+ "[ChosenInlineResult], [On*], and flow-step route. Contribute routes with the generated "
				+ "Add{Assembly}Telegram() instead of registering IUpdateRouter."),
		};
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
