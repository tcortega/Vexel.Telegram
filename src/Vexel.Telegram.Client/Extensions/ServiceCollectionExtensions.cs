using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;

namespace Vexel.Telegram.Client.Extensions;

/// <summary>
/// DI registration helpers for the Vexel Telegram client.
/// </summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Adds the Vexel Telegram client shell and a configured <see cref="ITelegramBotClient"/>.
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

		services.TryAddSingleton<ITelegramBotClient>(sp => new TelegramBotClient(tokenFactory(sp)));
		services.TryAddSingleton<VexelClient>();

		return services;
	}
}
