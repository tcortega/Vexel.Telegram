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
	/// <see cref="Feedback"/>, <see cref="Flow"/> / <see cref="IFlowStore"/>, and the
	/// <see cref="TelegramRouter"/>.
	/// Compose with Immediate <c>AddXxxHandlers()</c> and the generated
	/// <c>AddXxxTelegram()</c> route registration in the app.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="tokenFactory">Factory that returns the bot token.</param>
	/// <param name="configureClientOptions">Optional client options configuration.</param>
	/// <param name="configureFlowOptions">Optional flow options configuration.</param>
	/// <returns>The same service collection.</returns>
	public static IServiceCollection AddTelegramBot(
		this IServiceCollection services,
		Func<IServiceProvider, string> tokenFactory,
		Action<VexelClientOptions>? configureClientOptions = null,
		Action<FlowOptions>? configureFlowOptions = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(tokenFactory);

		_ = services.AddTelegramService(tokenFactory, configureClientOptions);
		_ = services.AddVexelUpdateContexts();
		_ = services.AddTelegramFlow(configureFlowOptions);
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
	internal static IServiceCollection AddTelegramRouter(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		// A foreign IUpdateRouter registered before this call would win the TryAdd below and silently
		// take routing away from TelegramRouter, so refuse it here instead of dispatching nothing.
		if (!services.Any(static d => d.ServiceType == typeof(TelegramRouter))
			&& services.Any(static d => d.ServiceType == typeof(IUpdateRouter)))
		{
			throw new InvalidOperationException(
				"An IUpdateRouter is already registered. Vexel dispatches through the single "
				+ "TelegramRouter: an app-registered IUpdateRouter replaces it and silently disables every "
				+ "[Command], [Callback], [InlineQuery], [ChosenInlineResult], [On*], and flow-step route. "
				+ "Contribute routes with the generated Add{Assembly}Telegram() instead of registering "
				+ "IUpdateRouter.");
		}

		// Single shared instance exposed as IUpdateRouter (pipeline), TelegramRouter (tests), and
		// IBotCommandCatalog (SetMyCommands).
		services.TryAddSingleton<TelegramRouter>();
		services.TryAddSingleton<IUpdateRouter>(static sp => sp.GetRequiredService<TelegramRouter>());
		services.TryAddSingleton<IBotCommandCatalog>(static sp => sp.GetRequiredService<TelegramRouter>());

		// B4: discharge default answerCallbackQuery / answerInlineQuery after the full pipeline
		// when Feedback did not answer.
		services.TryAddEnumerable(
			ServiceDescriptor.Singleton<IUpdateCompletionHook, AnswerObligation>());

		return services;
	}

	/// <summary>
	/// Registers <see cref="IFlowStore"/> (<see cref="MemoryFlowStore"/>), <see cref="Flow"/>,
	/// and <see cref="FlowOptions"/>. Used by <see cref="AddTelegramBot"/>; exposed for tests.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="configure">Optional flow options configuration.</param>
	/// <returns>The same service collection.</returns>
	internal static IServiceCollection AddTelegramFlow(
		this IServiceCollection services,
		Action<FlowOptions>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.TryAddSingleton(TimeProvider.System);
		services.TryAddSingleton<IFlowStore, MemoryFlowStore>();
		services.TryAddScoped<Flow>();

		if (configure is not null)
		{
			_ = services.Configure(configure);
		}
		else
		{
			_ = services.AddOptions<FlowOptions>();
		}

		return services;
	}

	/// <summary>
	/// Registers the write-once <see cref="UpdateContextHolder"/>, concrete update contexts,
	/// <see cref="Feedback"/>, and the dispatcher scope initializer. Used by
	/// <see cref="AddTelegramBot"/>; exposed for tests that wire the client manually.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <returns>The same service collection.</returns>
	internal static IServiceCollection AddVexelUpdateContexts(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.TryAddScoped<UpdateContextHolder>();
		services.TryAddScoped<Feedback>();

		// Package seam: Client dispatches through IUpdateScopeInitializer without referencing Handlers.
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
