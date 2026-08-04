using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Vexel.Telegram.Client;
using Vexel.Telegram.Client.Dispatch;
using Vexel.Telegram.Client.Webhook;

namespace Vexel.Telegram.Tests.Webhook;

public sealed class WebhookUpdateReceiverTests
{
	private const string Secret = "s3cret-token_01";

	[Fact]
	public async Task ProcessAsync_RejectsMissingSecret_With403()
	{
		var (receiver, _) = CreateReceiver();

		await using var body = Body(/*lang=json,strict*/ """{"update_id":1}""");
		var status = await receiver.ProcessAsync(body, secretTokenHeader: null);

		Assert.Equal(HttpStatusCode.Forbidden, status);
	}

	[Fact]
	public async Task ProcessAsync_RejectsWrongSecret_With403()
	{
		var (receiver, _) = CreateReceiver();

		await using var body = Body(/*lang=json,strict*/ """{"update_id":1}""");
		var status = await receiver.ProcessAsync(body, secretTokenHeader: "wrong-token");

		Assert.Equal(HttpStatusCode.Forbidden, status);
	}

	[Fact]
	public async Task ProcessAsync_AcceptsValidSecret_AndSchedulesUpdate()
	{
		var scheduled = new TaskCompletionSource<Update>(TaskCreationOptions.RunContinuationsAsynchronously);
		var (receiver, _) = CreateReceiver(onUpdate: update =>
		{
			_ = scheduled.TrySetResult(update);
			return Task.CompletedTask;
		});

		await using var body = Body(
			/*lang=json,strict*/ """{"update_id":42,"message":{"message_id":1,"date":1700000000,"chat":{"id":7,"type":"private"},"text":"hi"}}""");

		var status = await receiver.ProcessAsync(body, secretTokenHeader: Secret);

		Assert.Equal(HttpStatusCode.OK, status);
		var update = await scheduled.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Equal(42, update.Id);
		Assert.Equal("hi", update.Message?.Text);
	}

	[Fact]
	public async Task ProcessAsync_RejectsInvalidJson_With400()
	{
		var (receiver, _) = CreateReceiver();

		await using var body = Body("not-json");
		var status = await receiver.ProcessAsync(body, secretTokenHeader: Secret);

		Assert.Equal(HttpStatusCode.BadRequest, status);
	}

	[Fact]
	public void SecretTokensEqual_RejectsNullOrLengthMismatch()
	{
		Assert.False(WebhookUpdateReceiver.SecretTokensEqual(provided: null, expected: Secret));
		Assert.False(WebhookUpdateReceiver.SecretTokensEqual(provided: "", expected: Secret));
		Assert.False(WebhookUpdateReceiver.SecretTokensEqual(provided: "x", expected: Secret));
		Assert.True(WebhookUpdateReceiver.SecretTokensEqual(provided: Secret, expected: Secret));
	}

	private static (WebhookUpdateReceiver Receiver, ServiceProvider Provider) CreateReceiver(
		Func<Update, Task>? onUpdate = null)
	{
		var services = new ServiceCollection();
		_ = services.AddSingleton(Options.Create(new VexelClientOptions
		{
			ReceiveMode = TelegramReceiveMode.Webhook,
			Webhook = new WebhookOptions
			{
				Url = new Uri("https://example.test/telegram/webhook"),
				SecretToken = Secret,
			},
		}));
		_ = services.AddSingleton<IUpdateDispatcher>(new CallbackDispatcher(onUpdate));
		_ = services.AddSingleton(sp => new UpdateScheduler(
			sp.GetRequiredService<IUpdateDispatcher>(),
			sp.GetRequiredService<IOptions<VexelClientOptions>>(),
			NullLogger<UpdateScheduler>.Instance));
		_ = services.AddSingleton(sp => new WebhookUpdateReceiver(
			sp.GetRequiredService<UpdateScheduler>(),
			sp.GetRequiredService<IOptions<VexelClientOptions>>(),
			NullLogger<WebhookUpdateReceiver>.Instance));

		var provider = services.BuildServiceProvider();
		return (provider.GetRequiredService<WebhookUpdateReceiver>(), provider);
	}

	private static MemoryStream Body(string json) =>
		new(Encoding.UTF8.GetBytes(json));

	private sealed class CallbackDispatcher(Func<Update, Task>? onUpdate) : IUpdateDispatcher
	{
		public Task DispatchAsync(Update update, CancellationToken cancellationToken) =>
			onUpdate?.Invoke(update) ?? Task.CompletedTask;
	}
}
