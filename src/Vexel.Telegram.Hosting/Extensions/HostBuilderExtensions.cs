using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Vexel.Telegram.Client;
using Vexel.Telegram.Client.Extensions;

namespace Vexel.Telegram.Hosting.Extensions;

/// <summary>
/// Host builder extensions for Vexel Telegram.
/// </summary>
public static class HostBuilderExtensions
{
	/// <summary>
	/// Adds Vexel Telegram client services and a hosted <see cref="VexelService"/>.
	/// </summary>
	/// <param name="hostBuilder">The host builder.</param>
	/// <param name="tokenFactory">Factory that returns the bot token.</param>
	/// <param name="configureClientOptions">Optional client options configuration.</param>
	/// <returns>The same host builder.</returns>
	public static IHostBuilder AddTelegramService(
		this IHostBuilder hostBuilder,
		Func<IServiceProvider, string> tokenFactory,
		Action<VexelClientOptions>? configureClientOptions = null)
	{
		ArgumentNullException.ThrowIfNull(hostBuilder);

		_ = hostBuilder.ConfigureServices((_, services) =>
			services.AddTelegramService(tokenFactory, configureClientOptions));

		return hostBuilder;
	}

	/// <summary>
	/// Adds Vexel Telegram client services, automatic SetMyCommands registration, and a hosted
	/// <see cref="VexelService"/>.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="tokenFactory">Factory that returns the bot token.</param>
	/// <param name="configureClientOptions">Optional client options configuration.</param>
	/// <returns>The same service collection.</returns>
	public static IServiceCollection AddTelegramService(
		this IServiceCollection services,
		Func<IServiceProvider, string> tokenFactory,
		Action<VexelClientOptions>? configureClientOptions = null)
	{
		ArgumentNullException.ThrowIfNull(services);

		_ = services.AddVexelTelegramClient(tokenFactory, configureClientOptions);

		// SetMyCommands runs before the receive loop: registration order == start order.
		services.TryAddSingleton<SetMyCommandsInitializer>();
		_ = services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<SetMyCommandsInitializer>());

		services.TryAddSingleton<VexelService>();
		_ = services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<VexelService>());

		return services;
	}
}
