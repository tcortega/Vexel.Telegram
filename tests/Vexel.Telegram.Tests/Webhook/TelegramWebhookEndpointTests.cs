using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Vexel.Telegram.AspNetCore.Extensions;
using Vexel.Telegram.Client;
using Vexel.Telegram.Client.Extensions;
using Vexel.Telegram.Client.Webhook;
using Vexel.Telegram.Tests.Fakes;

namespace Vexel.Telegram.Tests.Webhook;

public sealed class TelegramWebhookEndpointTests
{
	[Fact]
	public async Task MapTelegramWebhook_UsesConfiguredPath_AndAllowsAnonymous()
	{
		await using var app = CreateApp("/hooks/tg");

		var endpoint = Assert.IsType<RouteEndpoint>(
			Assert.Single(((IEndpointRouteBuilder)app).DataSources.SelectMany(static d => d.Endpoints)));

		Assert.Equal("hooks/tg", endpoint.RoutePattern.RawText?.TrimStart('/'));
		Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
		Assert.Contains("POST", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
	}

	[Fact]
	public async Task MapTelegramWebhook_PatternOverride_WinsOverOptions()
	{
		await using var app = CreateApp("/hooks/tg");
		_ = app.MapTelegramWebhook("/override");

		var patterns = ((IEndpointRouteBuilder)app).DataSources
			.SelectMany(static d => d.Endpoints)
			.OfType<RouteEndpoint>()
			.Select(static e => e.RoutePattern.RawText?.TrimStart('/'))
			.ToArray();

		Assert.Contains("override", patterns);
	}

	private static WebApplication CreateApp(string webhookPath)
	{
		var builder = WebApplication.CreateSlimBuilder();
		_ = builder.Services.AddSingleton<ITelegramBotClient>(new RecordingTelegramBotClient());
		_ = builder.Services.AddVexelTelegramClient(
			static _ => "token",
			options =>
			{
				options.ReceiveMode = TelegramReceiveMode.Webhook;
				options.Webhook = new WebhookOptions
				{
					Url = new Uri("https://example.test/hooks/tg"),
					SecretToken = "s3cret-token",
					Path = webhookPath,
				};
			});

		var app = builder.Build();
		_ = app.MapTelegramWebhook();

		return app;
	}
}
