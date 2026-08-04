using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Vexel.Telegram.Client.Dispatch;
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

		var routers = provider.GetServices<IUpdateRouter>();

		_ = Assert.Single(routers);
		Assert.Same(provider.GetRequiredService<TelegramRouter>(), Assert.Single(routers));
	}

	[Fact]
	public void App_registered_router_does_not_suppress_the_Telegram_router()
	{
		using var provider = BuildProvider(static services =>
		{
			_ = services.AddSingleton<IUpdateRouter, NoopRouter>();
			_ = services.AddTelegramRouter();
		});

		var routers = provider.GetServices<IUpdateRouter>();

		Assert.Contains(routers, static r => r is NoopRouter);
		Assert.Contains(routers, static r => r is TelegramRouter);
	}

	[Fact]
	public void Telegram_router_resolves_when_an_app_router_is_registered_last()
	{
		using var provider = BuildProvider(static services =>
		{
			_ = services.AddTelegramRouter();
			_ = services.AddSingleton<IUpdateRouter, NoopRouter>();
		});

		var router = provider.GetRequiredService<TelegramRouter>();

		Assert.Contains(provider.GetServices<IUpdateRouter>(), router1 => ReferenceEquals(router1, router));
	}

	private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
	{
		var services = new ServiceCollection();
		_ = services.AddLogging();
		_ = services.AddSingleton<ITelegramBotClient>(new RecordingTelegramBotClient());
		configure(services);
		return services.BuildServiceProvider(validateScopes: true);
	}

	private sealed class NoopRouter : IUpdateRouter
	{
		public Task RouteAsync(Update update, IServiceProvider scope, CancellationToken cancellationToken) =>
			Task.CompletedTask;
	}
}
