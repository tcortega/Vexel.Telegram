using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Vexel.Telegram.Client;
using Vexel.Telegram.Client.Dispatch;
using Vexel.Telegram.Client.Webhook;
using Vexel.Telegram.Tests.Fakes;

namespace Vexel.Telegram.Tests.Webhook;

public sealed class ReceiveModeValidationTests
{
	[Fact]
	public void Polling_Default_IsValid()
	{
		var options = new VexelClientOptions();
		options.ValidateReceiveMode();
	}

	[Fact]
	public void Polling_WithWebhookConfigured_FailsFast()
	{
		var options = new VexelClientOptions
		{
			ReceiveMode = TelegramReceiveMode.Polling,
			Webhook = new WebhookOptions
			{
				Url = new Uri("https://example.test/hook"),
				SecretToken = "secret",
			},
		};

		var ex = Assert.Throws<InvalidOperationException>(options.ValidateReceiveMode);
		Assert.Contains("both polling and webhook", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Webhook_WithoutOptions_FailsFast()
	{
		var options = new VexelClientOptions
		{
			ReceiveMode = TelegramReceiveMode.Webhook,
			Webhook = null,
		};

		var ex = Assert.Throws<InvalidOperationException>(options.ValidateReceiveMode);
		Assert.Contains("Webhook is null", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Webhook_WithoutSecret_FailsFast()
	{
		var options = new VexelClientOptions
		{
			ReceiveMode = TelegramReceiveMode.Webhook,
			Webhook = new WebhookOptions
			{
				Url = new Uri("https://example.test/hook"),
				SecretToken = "",
			},
		};

		var ex = Assert.Throws<InvalidOperationException>(options.ValidateReceiveMode);
		Assert.Contains("SecretToken", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Webhook_WithoutUrl_FailsFast()
	{
		var options = new VexelClientOptions
		{
			ReceiveMode = TelegramReceiveMode.Webhook,
			Webhook = new WebhookOptions
			{
				Url = null,
				SecretToken = "secret",
			},
		};

		var ex = Assert.Throws<InvalidOperationException>(options.ValidateReceiveMode);
		Assert.Contains("Url", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Webhook_InvalidSecretCharset_FailsFast()
	{
		var options = new VexelClientOptions
		{
			ReceiveMode = TelegramReceiveMode.Webhook,
			Webhook = new WebhookOptions
			{
				Url = new Uri("https://example.test/hook"),
				SecretToken = "bad secret!",
			},
		};

		var ex = Assert.Throws<InvalidOperationException>(options.ValidateReceiveMode);
		Assert.Contains("1-256", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Webhook_Valid_Passes()
	{
		var options = new VexelClientOptions
		{
			ReceiveMode = TelegramReceiveMode.Webhook,
			Webhook = new WebhookOptions
			{
				Url = new Uri("https://example.test/telegram/webhook"),
				SecretToken = "AbC_12-xyz",
				Path = "/telegram/webhook",
			},
		};

		options.ValidateReceiveMode();
	}

	[Fact]
	public async Task VexelClient_RunAsync_DualMode_FailsFast()
	{
		var services = new ServiceCollection();
		_ = services.AddLogging();
		_ = services.AddSingleton<ITelegramBotClient>(new RecordingTelegramBotClient());
		_ = services.AddSingleton(Options.Create(new VexelClientOptions
		{
			ReceiveMode = TelegramReceiveMode.Polling,
			Webhook = new WebhookOptions
			{
				Url = new Uri("https://example.test/hook"),
				SecretToken = "secret",
			},
		}));
		_ = services.AddSingleton(sp => new UpdateScheduler(
			new NoopDispatcher(),
			sp.GetRequiredService<IOptions<VexelClientOptions>>(),
			NullLogger<UpdateScheduler>.Instance));
		_ = services.AddSingleton<VexelClient>();

		await using var provider = services.BuildServiceProvider();
		var client = provider.GetRequiredService<VexelClient>();

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => client.RunAsync(CancellationToken.None));
		Assert.Contains("both polling and webhook", ex.Message, StringComparison.Ordinal);
	}

	private sealed class NoopDispatcher : IUpdateDispatcher
	{
		public Task DispatchAsync(Update update, CancellationToken cancellationToken) =>
			Task.CompletedTask;
	}
}
