using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types;
using Vexel.Telegram.Client;
using Vexel.Telegram.Client.Dispatch;
using Vexel.Telegram.Client.Extensions;
using Vexel.Telegram.Handlers.DependencyInjection;
using Vexel.Telegram.Handlers.Routing;
using Vexel.Telegram.Tests.Fakes;

namespace Vexel.Telegram.Tests.Routing;

public sealed class TelegramRouterRegistrationTests
{
	[Fact]
	public void Router_is_registered_once_even_when_called_twice()
	{
		using var provider = BuildProvider(static services =>
		{
			_ = services.AddTelegramRouter();
			_ = services.AddTelegramRouter();
		});

		var router = provider.GetRequiredService<IUpdateRouter>();
		Assert.Same(provider.GetRequiredService<TelegramRouter>(), router);
		Assert.Same(router, provider.GetRequiredService<IBotCommandCatalog>());
	}

	[Fact]
	public void Telegram_router_is_the_single_IUpdateRouter()
	{
		using var provider = BuildProvider(static services =>
		{
			_ = services.AddTelegramRouter();
		});

		Assert.IsType<TelegramRouter>(provider.GetRequiredService<IUpdateRouter>());
		Assert.Same(
			provider.GetRequiredService<TelegramRouter>(),
			provider.GetRequiredService<IUpdateRouter>());
	}

	[Fact]
	public void Foreign_router_registered_before_the_router_fails_fast()
	{
		var services = new ServiceCollection();
		_ = services.AddSingleton<IUpdateRouter, ForeignRouter>();

		var ex = Assert.Throws<InvalidOperationException>(() =>
		{
			_ = services.AddTelegramRouter();
		});
		Assert.Contains("IUpdateRouter is already registered", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Foreign_router_registered_after_the_router_fails_the_dispatcher_resolve()
	{
		using var provider = BuildProvider(static services =>
		{
			_ = services.AddVexelTelegramClient(static _ => "token");
			_ = services.AddTelegramRouter();
			_ = services.AddSingleton<IUpdateRouter, ForeignRouter>();
		});

		var ex = Assert.Throws<InvalidOperationException>(() =>
		{
			_ = provider.GetRequiredService<UpdateDispatcher>();
		});
		Assert.Contains("Multiple IUpdateRouter services are registered", ex.Message, StringComparison.Ordinal);
	}

	private sealed class ForeignRouter : IUpdateRouter
	{
		public Task RouteAsync(Update update, IServiceProvider scope, CancellationToken cancellationToken) =>
			Task.CompletedTask;
	}

	private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
	{
		var services = new ServiceCollection();
		_ = services.AddLogging();
		_ = services.AddSingleton<ITelegramBotClient>(new RecordingTelegramBotClient());
		configure(services);
		return services.BuildServiceProvider(validateScopes: true);
	}
}
