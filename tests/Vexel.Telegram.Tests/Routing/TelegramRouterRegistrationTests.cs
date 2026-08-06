using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Vexel.Telegram.Client;
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

	private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
	{
		var services = new ServiceCollection();
		_ = services.AddLogging();
		_ = services.AddSingleton<ITelegramBotClient>(new RecordingTelegramBotClient());
		configure(services);
		return services.BuildServiceProvider(validateScopes: true);
	}
}
