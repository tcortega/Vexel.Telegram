using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vexel.Telegram.Client;
using Vexel.Telegram.Client.Dispatch;
using Vexel.Telegram.Handlers.Contexts;
using Vexel.Telegram.Handlers.Routing;
using Vexel.Telegram.Hosting.Extensions;

namespace Vexel.Telegram.Handlers.DependencyInjection;

/// <summary>
/// DI registration helpers for Vexel Telegram bot services.
/// </summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Adds the Vexel Telegram client, hosted receive loop, per-update contexts,
	/// <see cref="Feedback"/>, and the <see cref="TelegramRouter"/>.
	/// Compose with Immediate <c>AddXxxHandlers()</c> and the generated
	/// <c>AddXxxTelegram()</c> route registration in the app.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="tokenFactory">Factory that returns the bot token.</param>
	/// <param name="configureClientOptions">Optional client options configuration.</param>
	/// <returns>The same service collection.</returns>
	public static IServiceCollection AddTelegramBot(
		this IServiceCollection services,
		Func<IServiceProvider, string> tokenFactory,
		Action<VexelClientOptions>? configureClientOptions = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(tokenFactory);

		_ = services.AddTelegramService(tokenFactory, configureClientOptions);
		_ = services.AddVexelUpdateContexts();
		_ = services.AddTelegramRouter();

		return services;
	}

	/// <summary>
	/// Registers <see cref="TelegramRouter"/> as the routed stage of the update pipeline.
	/// Used by <see cref="AddTelegramBot"/>; exposed for tests that wire the client manually.
	/// Call generated <c>AddXxxTelegram()</c> methods to contribute routes before building the provider.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <returns>The same service collection.</returns>
	public static IServiceCollection AddTelegramRouter(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		// Single shared instance exposed both as IUpdateRouter (pipeline) and TelegramRouter (tests/SetMyCommands).
		services.TryAddSingleton<IUpdateRouter, TelegramRouter>();
		services.TryAddSingleton(static sp => (TelegramRouter)sp.GetRequiredService<IUpdateRouter>());

		return services;
	}

	/// <summary>
	/// Registers the write-once <see cref="UpdateContextHolder"/>, concrete update contexts,
	/// <see cref="Feedback"/>, and the dispatcher scope initializer. Used by
	/// <see cref="AddTelegramBot"/>; exposed for tests that wire the client manually.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <returns>The same service collection.</returns>
	public static IServiceCollection AddVexelUpdateContexts(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.TryAddScoped<UpdateContextHolder>();
		services.TryAddScoped<Feedback>();

		// The dispatcher runs every IUpdateScopeInitializer, so this must compose with app-registered
		// initializers instead of being skipped when one is already present.
		services.TryAddEnumerable(
			ServiceDescriptor.Scoped<IUpdateScopeInitializer, UpdateContextScopeInitializer>());

		// Concrete contexts are scoped factories over the holder so Immediate injects them as
		// ordinary DI services. Wrong-kind resolution fails with a clear message.
		_ = services.AddScoped(static sp =>
			sp.GetRequiredService<UpdateContextHolder>().Message
			?? throw new InvalidOperationException(
				"MessageContext is not available for this update kind."));

		_ = services.AddScoped(static sp =>
			sp.GetRequiredService<UpdateContextHolder>().Callback
			?? throw new InvalidOperationException(
				"CallbackContext is not available for this update kind."));

		_ = services.AddScoped(static sp =>
			sp.GetRequiredService<UpdateContextHolder>().InlineQuery
			?? throw new InvalidOperationException(
				"InlineQueryContext is not available for this update kind."));

		_ = services.AddScoped(static sp =>
			sp.GetRequiredService<UpdateContextHolder>().ChosenInlineResult
			?? throw new InvalidOperationException(
				"ChosenInlineResultContext is not available for this update kind."));

		return services;
	}
}
