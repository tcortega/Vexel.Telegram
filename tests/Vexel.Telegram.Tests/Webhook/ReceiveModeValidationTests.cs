using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
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
			static (_, _) => Task.CompletedTask,
			sp.GetRequiredService<IOptions<VexelClientOptions>>(),
			NullLogger<UpdateScheduler>.Instance));
		_ = services.AddSingleton<VexelClient>();

		await using var provider = services.BuildServiceProvider();
		var client = provider.GetRequiredService<VexelClient>();

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => client.RunAsync(CancellationToken.None));
		Assert.Contains("both polling and webhook", ex.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("/telegram/webhook", "/telegram/webhook", true)]
	[InlineData("/telegram/webhook", "telegram/webhook", true)]
	[InlineData("/Telegram/Webhook", "/telegram/webhook", true)]
	[InlineData("/", "/telegram/webhook", false)]
	[InlineData("/hooks/tg", "/telegram/webhook", false)]
	public void WebhookPathsMatch_IgnoresSlashesAndCase(string urlPath, string mappedPath, bool expected) =>
		Assert.Equal(expected, VexelClient.WebhookPathsMatch(urlPath, mappedPath));

	[Fact]
	public async Task VexelClient_RunAsync_WebhookUrlPathMismatch_WarnsAndContinues()
	{
		var logger = new RecordingLogger<VexelClient>();
		var options = Options.Create(new VexelClientOptions
		{
			ReceiveMode = TelegramReceiveMode.Webhook,
			Webhook = new WebhookOptions
			{
				Url = new Uri("https://example.test/"),
				SecretToken = "AbC_12-xyz",
				Path = "/telegram/webhook",
			},
		});

		await using var scheduler = new UpdateScheduler(
			static (_, _) => Task.CompletedTask,
			options,
			NullLogger<UpdateScheduler>.Instance);
		var bot = new RecordingTelegramBotClient();
		var client = new VexelClient(logger, bot, options, scheduler);

		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		await client.RunAsync(cts.Token);

		_ = Assert.Single(bot.OfType<SetWebhookRequest>());
		Assert.Contains(
			logger.Entries,
			static e => e.Level == LogLevel.Warning
				&& e.Message.Contains("differs from the mapped endpoint path", StringComparison.Ordinal));
	}

	[Fact]
	public async Task VexelClient_RunAsync_WebhookUrlPathMatches_DoesNotWarn()
	{
		var logger = new RecordingLogger<VexelClient>();
		var options = Options.Create(new VexelClientOptions
		{
			ReceiveMode = TelegramReceiveMode.Webhook,
			Webhook = new WebhookOptions
			{
				Url = new Uri("https://example.test/telegram/webhook"),
				SecretToken = "AbC_12-xyz",
				Path = "/telegram/webhook",
			},
		});

		await using var scheduler = new UpdateScheduler(
			static (_, _) => Task.CompletedTask,
			options,
			NullLogger<UpdateScheduler>.Instance);
		var client = new VexelClient(logger, new RecordingTelegramBotClient(), options, scheduler);

		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		await client.RunAsync(cts.Token);

		Assert.DoesNotContain(logger.Entries, static e => e.Level == LogLevel.Warning);
	}

	/// <summary>
	/// A stop landing while <c>setWebhook</c> is in flight is normal shutdown, not a fatal client
	/// error: Telegram.Bot wraps the cancelled request in <c>RequestException</c>, which would
	/// otherwise reach <c>VexelService</c> and take the host down on every webhook shutdown.
	/// </summary>
	[Fact]
	public async Task VexelClient_RunAsync_WebhookCancelledMidSetWebhook_StopsQuietly()
	{
		var options = Options.Create(new VexelClientOptions
		{
			ReceiveMode = TelegramReceiveMode.Webhook,
			Webhook = new WebhookOptions
			{
				Url = new Uri("https://example.test/telegram/webhook"),
				SecretToken = "AbC_12-xyz",
				Path = "/telegram/webhook",
			},
		});

		await using var scheduler = new UpdateScheduler(
			static (_, _) => Task.CompletedTask,
			options,
			NullLogger<UpdateScheduler>.Instance);
		var bot = new RecordingTelegramBotClient
		{
			FailRequest = static request => request is SetWebhookRequest
				? new RequestException(
					"There was an exception during deserialization of the response",
					new TaskCanceledException("A task was canceled."))
				: null,
		};
		var client = new VexelClient(NullLogger<VexelClient>.Instance, bot, options, scheduler);

		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		await client.RunAsync(cts.Token);

		_ = Assert.Single(bot.OfType<SetWebhookRequest>());
	}

	/// <summary>Same race on the polling side, where <c>deleteWebhook</c> opens the receive loop.</summary>
	[Fact]
	public async Task VexelClient_RunAsync_PollingCancelledMidDeleteWebhook_StopsQuietly()
	{
		var options = Options.Create(new VexelClientOptions());

		await using var scheduler = new UpdateScheduler(
			static (_, _) => Task.CompletedTask,
			options,
			NullLogger<UpdateScheduler>.Instance);
		var bot = new RecordingTelegramBotClient
		{
			FailRequest = static request => request is DeleteWebhookRequest
				? new RequestException(
					"There was an exception during deserialization of the response",
					new TaskCanceledException("A task was canceled."))
				: null,
		};
		var client = new VexelClient(NullLogger<VexelClient>.Instance, bot, options, scheduler);

		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		await client.RunAsync(cts.Token);

		_ = Assert.Single(bot.OfType<DeleteWebhookRequest>());
	}

	/// <summary>A genuine failure is still fatal: cancellation only excuses cancelled calls.</summary>
	[Fact]
	public async Task VexelClient_RunAsync_NonCancellationFailure_StaysFatal()
	{
		var options = Options.Create(new VexelClientOptions());

		await using var scheduler = new UpdateScheduler(
			static (_, _) => Task.CompletedTask,
			options,
			NullLogger<UpdateScheduler>.Instance);
		var bot = new RecordingTelegramBotClient
		{
			FailRequest = static request => request is DeleteWebhookRequest
				? new RequestException("Telegram API unreachable")
				: null,
		};
		var client = new VexelClient(NullLogger<VexelClient>.Instance, bot, options, scheduler);

		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		_ = await Assert.ThrowsAsync<RequestException>(() => client.RunAsync(cts.Token));
	}

}
