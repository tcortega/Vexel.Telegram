using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Vexel.Telegram.Client;
using Vexel.Telegram.Client.Extensions;
using Vexel.Telegram.Hosting;
using Xunit.Abstractions;

namespace Vexel.Telegram.Tests.EndToEnd;

/// <summary>
/// End-to-end: a real generic host runs <see cref="VexelService"/> against a fake Telegram Bot API
/// server, so updates travel the whole path a live bot uses (HTTP long poll -> receive loop ->
/// per-chat lanes -> handler).
/// </summary>
public sealed class PollingEndToEndTests(ITestOutputHelper output)
{
	private const string Token = "424242:AAHkQqL2b0zGf4nZ0K2QYQ0F0uSj1sM6Q8w";

	[Fact]
	public async Task Bot_OrdersPerChat_RunsChatsInParallel_IsolatesFaults_AndDrainsOnShutdown()
	{
		var transcript = new Transcript();
		var workload = new ScriptedWorkload();

		var slowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var shutdownWorkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		// chat 100: slow first message, then a throwing one, then a normal one.
		workload.Script(902, async ct =>
		{
			_ = slowStarted.TrySetResult();
			await Task.Delay(400, ct);
		});
		workload.Script(903, _ => throw new InvalidOperationException("handler blew up"));
		workload.Script(904, _ => Task.CompletedTask);

		// chat 200: a quick message that must not wait behind chat 100.
		workload.Script(905, _ => Task.CompletedTask);

		// arrives last and is still running when the host is asked to stop.
		workload.Script(906, async ct =>
		{
			_ = shutdownWorkStarted.TrySetResult();
			await Task.Delay(400, ct);
		});

		await using var api = new FakeTelegramBotApi(transcript.Write);

		// Backlog that piled up while the bot was offline; DropPendingUpdates=true must skip it.
		api.Enqueue(
			FakeTelegramBotApi.MessageUpdate(900, chatId: 100, "stale backlog 1"),
			FakeTelegramBotApi.MessageUpdate(901, chatId: 100, "stale backlog 2"));
		api.Start();

		transcript.Write($"fake Telegram Bot API listening on {api.BaseAddress} with 2 stale updates queued");

		using var host = BuildHost(api.BaseAddress, transcript, workload);
		await host.StartAsync();

		// Wait until the receiver is past the backlog probe and polling for live updates.
		await WaitForAsync(() => api.GetUpdatesCalls >= 2);

		transcript.Write("user sends 3 messages in chat 100 and 1 message in chat 200 (single poll batch)");
		api.Enqueue(
			FakeTelegramBotApi.MessageUpdate(902, chatId: 100, "/slow"),
			FakeTelegramBotApi.MessageUpdate(903, chatId: 100, "/boom"),
			FakeTelegramBotApi.MessageUpdate(904, chatId: 100, "/after-boom"),
			FakeTelegramBotApi.MessageUpdate(905, chatId: 200, "/hello from another chat"));

		await WaitForAsync(() => transcript.Contains("update 904 END") && transcript.Contains("update 905 END"));

		transcript.Write("user sends 1 more message in chat 100, then the host is asked to shut down");
		api.Enqueue(FakeTelegramBotApi.MessageUpdate(906, chatId: 100, "/in-flight at shutdown"));
		await shutdownWorkStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

		await host.StopAsync();
		transcript.Write("host stopped");

		foreach (var line in transcript.Lines)
		{
			output.WriteLine(line);
		}

		output.WriteLine(string.Empty);
		output.WriteLine("Telegram Bot API calls served:");
		foreach (var request in api.Requests)
		{
			output.WriteLine($"  {request}");
		}

		// DropPendingUpdates=true (default): the offline backlog never reaches a handler.
		Assert.False(transcript.Contains("update 900 START"));
		Assert.False(transcript.Contains("update 901 START"));

		// Same chat stays strictly ordered and serialized: no update starts before the previous ends.
		Assert.True(transcript.IndexOf("update 902 END") < transcript.IndexOf("update 903 START"));
		Assert.True(transcript.IndexOf("update 903 THREW") < transcript.IndexOf("update 904 START"));

		// A throwing handler is isolated: the lane survives and the next update in the same chat runs.
		Assert.True(transcript.Contains("update 903 THREW InvalidOperationException"));
		Assert.True(transcript.Contains("Raw update handler"));
		Assert.True(transcript.Contains("update 904 END"));

		// Another chat is not blocked by the slow handler in chat 100.
		Assert.True(slowStarted.Task.IsCompletedSuccessfully);
		Assert.True(transcript.IndexOf("update 905 END") < transcript.IndexOf("update 902 END"));

		// Work already in flight is drained during shutdown instead of being dropped.
		Assert.True(transcript.Contains("update 906 END"));
		Assert.True(transcript.IndexOf("update 906 END") < transcript.IndexOf("host stopped"));
	}

	[Fact]
	public async Task Bot_UnderBurst_NeverDropsUpdates_WhenLanesAreFull()
	{
		const int FirstId = 902;
		const int BurstSize = 20;

		var transcript = new Transcript();
		var workload = new ScriptedWorkload();
		for (var id = FirstId; id < FirstId + BurstSize; id++)
		{
			workload.Script(id, ct => Task.Delay(15, ct));
		}

		await using var api = new FakeTelegramBotApi();
		api.Enqueue(FakeTelegramBotApi.MessageUpdate(901, chatId: 100, "stale backlog"));
		api.Start();

		// Lane capacity 2 with a 20-update burst: the receive loop must apply backpressure, not drop.
		using var host = BuildHost(
			api.BaseAddress,
			transcript,
			workload,
			options => options.LaneCapacity = 2);

		await host.StartAsync();
		await WaitForAsync(() => api.GetUpdatesCalls >= 2);

		transcript.Write($"user floods chat 100 with {BurstSize} messages while LaneCapacity=2");
		api.Enqueue(
		[
			.. Enumerable
				.Range(FirstId, BurstSize)
				.Select(id => FakeTelegramBotApi.MessageUpdate(id, chatId: 100, $"burst {id}")),
		]);

		await WaitForAsync(() => HandledIds(transcript).Count == BurstSize);
		await host.StopAsync();

		foreach (var line in transcript.Lines)
		{
			output.WriteLine(line);
		}

		// Every update survived the full lane and arrived in send order.
		Assert.Equal([.. Enumerable.Range(FirstId, BurstSize)], HandledIds(transcript));
	}

	private static List<int> HandledIds(Transcript transcript) =>
	[
		.. transcript.Lines
			.Where(line => line.EndsWith(" END", StringComparison.Ordinal))
			.Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^2])
			.Select(id => int.Parse(id, CultureInfo.InvariantCulture)),
	];

	private static IHost BuildHost(
		string apiAddress,
		Transcript transcript,
		ScriptedWorkload workload,
		Action<VexelClientOptions>? configureOptions = null)
	{
		var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

		_ = builder.Logging.SetMinimumLevel(LogLevel.Information);
		_ = builder.Services.AddSingleton<ILoggerProvider>(_ => new TranscriptLoggerProvider(transcript));

		_ = builder.Services.AddSingleton(transcript);
		_ = builder.Services.AddSingleton(workload);

		// Point the real Telegram.Bot client at the fake API instead of api.telegram.org.
		_ = builder.Services.AddSingleton<ITelegramBotClient>(
			_ => new TelegramBotClient(new TelegramBotClientOptions(Token, apiAddress)));

		_ = builder.Services.AddVexelTelegramClient(_ => Token, configureOptions);
		_ = builder.Services.AddRawUpdateHandler<ScriptedRawHandler>();
		_ = builder.Services.AddHostedService<VexelService>();

		return builder.Build();
	}

	private static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
	{
		var limit = timeout ?? TimeSpan.FromSeconds(20);
		var start = DateTime.UtcNow;
		while (!condition())
		{
			if (DateTime.UtcNow - start > limit)
			{
				throw new TimeoutException("Condition was not met within the allotted time.");
			}

			await Task.Delay(20);
		}
	}

	/// <summary>Per-update behaviour the bot's handler should perform.</summary>
	public sealed class ScriptedWorkload
	{
		private readonly ConcurrentDictionary<int, Func<CancellationToken, Task>> _script = [];

		public void Script(int updateId, Func<CancellationToken, Task> work) =>
			_script[updateId] = work;

		public Task RunAsync(int updateId, CancellationToken cancellationToken) =>
			_script.TryGetValue(updateId, out var work) ? work(cancellationToken) : Task.CompletedTask;
	}

	/// <summary>The bot's own handler, registered exactly as a consumer would register one.</summary>
	public sealed class ScriptedRawHandler(Transcript transcript, ScriptedWorkload workload) : IRawUpdateHandler
	{
		public async Task HandleAsync(Update update, CancellationToken cancellationToken)
		{
			var chatId = update.Message!.Chat.Id;
			transcript.Write(string.Create(
				CultureInfo.InvariantCulture,
				$"bot handler   chat {chatId} update {update.Id} START  \"{update.Message.Text}\""));

			try
			{
				await workload.RunAsync(update.Id, cancellationToken);
			}
			catch (Exception ex)
			{
				transcript.Write(string.Create(
					CultureInfo.InvariantCulture,
					$"bot handler   chat {chatId} update {update.Id} THREW {ex.GetType().Name}"));

				throw;
			}

			transcript.Write(string.Create(
				CultureInfo.InvariantCulture,
				$"bot handler   chat {chatId} update {update.Id} END"));
		}
	}
}
