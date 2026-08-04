using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.ReplyMarkups;
using Vexel.Telegram.Client;
using Vexel.Telegram.Client.Extensions;
using Vexel.Telegram.Handlers.Contexts;
using Vexel.Telegram.Handlers.DependencyInjection;
using Xunit.Abstractions;
using FeedbackService = Vexel.Telegram.Handlers.Feedback;

namespace Vexel.Telegram.Tests.EndToEnd;

/// <summary>
/// End-to-end: a real generic host wired with <c>AddTelegramBot</c> runs against a fake Telegram
/// Bot API server, so a bot author's handlers receive the concrete contexts and talk back through
/// <see cref="FeedbackService"/> over real HTTP - the same path a live bot uses.
/// </summary>
public sealed class ContextsEndToEndTests(ITestOutputHelper output)
{
	private const string Token = "424242:AAHkQqL2b0zGf4nZ0K2QYQ0F0uSj1sM6Q8w";

	[Fact]
	public async Task Bot_BindsContextsPerUpdate_TalksBackThroughFeedback_AndIsolatesEveryUpdateScope()
	{
		var transcript = new Transcript();
		var log = new ScopeLog(transcript);

		await using var api = new FakeTelegramBotApi(transcript.Write);
		api.Start();
		transcript.Write($"fake Telegram Bot API listening on {api.BaseAddress}");

		using var host = BuildHost(api.BaseAddress, transcript, log);
		await host.StartAsync();
		await WaitForAsync(() => api.GetUpdatesCalls >= 2);

		transcript.Write("user in chat 100 sends \"/menu\"");
		api.Enqueue(FakeTelegramBotApi.MessageUpdate(902, chatId: 100, "/menu"));
		await WaitForAsync(() => log.Handled("message:902"));

		transcript.Write("user in chat 100 taps the \"Blue\" button on the bot's menu message");
		api.Enqueue(FakeTelegramBotApi.CallbackQueryUpdate(
			903,
			chatId: 100,
			botMessageId: 9001,
			callbackQueryId: "cbq-1",
			data: "pick:blue"));
		await WaitForAsync(() => log.Handled("callback:903"));

		transcript.Write("a different user in chat 200 sends \"/menu\"");
		api.Enqueue(FakeTelegramBotApi.MessageUpdate(904, chatId: 200, "/menu"));
		await WaitForAsync(() => log.Handled("message:904"));

		transcript.Write("user 700 types an inline query \"vex\"");
		api.Enqueue(FakeTelegramBotApi.InlineQueryUpdate(905, userId: 700, "iq-1", "vex"));
		await WaitForAsync(() => log.Handled("inline:905"));

		await host.StopAsync();
		transcript.Write("host stopped");

		foreach (var line in transcript.Lines)
		{
			output.WriteLine(line);
		}

		output.WriteLine(string.Empty);
		output.WriteLine("Bot -> Telegram Bot API calls, as the Telegram side saw them:");
		foreach (var call in api.OutboundCalls)
		{
			output.WriteLine($"  {call}");
		}

		output.WriteLine(string.Empty);
		output.WriteLine("Per-update DI scope identities:");
		foreach (var binding in log.Bindings)
		{
			output.WriteLine($"  {binding}");
		}

		var calls = api.OutboundCalls;

		// The message update reached a handler that injected MessageContext, and Feedback sent the
		// menu back to that chat with the keyboard attached.
		Assert.Contains(
			calls,
			call => call.StartsWith("sendMessage chat_id=100", StringComparison.Ordinal)
				&& call.Contains("\"pick one\"", StringComparison.Ordinal)
				&& call.Contains("reply_markup=<inline keyboard>", StringComparison.Ordinal));

		// A second chat is bound to its own context, not chat 100's.
		Assert.Contains(
			calls,
			call => call.StartsWith("sendMessage chat_id=200", StringComparison.Ordinal)
				&& call.Contains("\"pick one\"", StringComparison.Ordinal));

		// The callback handler answered twice; Feedback is first-wins idempotent, so Telegram saw one.
		var answers = calls.Where(static c => c.StartsWith("answerCallbackQuery", StringComparison.Ordinal)).ToArray();
		var answer = Assert.Single(answers);
		Assert.Contains("callback_query_id=\"cbq-1\"", answer, StringComparison.Ordinal);
		Assert.Contains("\"Blue it is\"", answer, StringComparison.Ordinal);

		// Edit targets the bot-authored message the button hangs off.
		Assert.Contains(
			calls,
			call => call.StartsWith("editMessageText chat_id=100 message_id=9001", StringComparison.Ordinal)
				&& call.Contains("\"You picked blue\"", StringComparison.Ordinal));

		// Chatless inline query is answered by inline query id.
		Assert.Contains(
			calls,
			call => call.StartsWith("answerInlineQuery", StringComparison.Ordinal)
				&& call.Contains("inline_query_id=\"iq-1\"", StringComparison.Ordinal));

		// Wrong-kind handlers never ran: MessageContext handlers stay out of callback/inline updates.
		Assert.Equal(
			["message:902", "callback:903", "message:904", "inline:905"],
			[.. log.HandledKeys]);

		// Per-update scope isolation: every update got its own holder, Feedback, and bound update.
		var scopes = log.Scopes;
		Assert.Equal(4, scopes.Count);
		Assert.Equal(4, scopes.Select(static s => s.HolderId).Distinct().Count());
		Assert.Equal(4, scopes.Select(static s => s.FeedbackId).Distinct().Count());
		Assert.Equal([902, 903, 904, 905], [.. scopes.Select(static s => s.BoundUpdateId)]);

		// The idempotence latch is per-update state, not leaked to the next update's Feedback.
		Assert.Equal(
			[false, true, false, false],
			[.. scopes.Select(static s => s.CallbackAnsweredAtEnd)]);
	}

	private static IHost BuildHost(string apiAddress, Transcript transcript, ScopeLog log)
	{
		var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

		_ = builder.Logging.SetMinimumLevel(LogLevel.Information);
		_ = builder.Services.AddSingleton<ILoggerProvider>(_ => new TranscriptLoggerProvider(transcript));

		_ = builder.Services.AddSingleton(transcript);
		_ = builder.Services.AddSingleton(log);

		// Point the real Telegram.Bot client at the fake API instead of api.telegram.org.
		_ = builder.Services.AddSingleton<ITelegramBotClient>(
			_ => new TelegramBotClient(new TelegramBotClientOptions(Token, apiAddress)));

		// The app DI entry point under test: client + hosted receive loop + contexts + Feedback.
		_ = builder.Services.AddTelegramBot(_ => Token);

		_ = builder.Services.AddRawUpdateHandler<MenuHandler>();
		_ = builder.Services.AddRawUpdateHandler<ButtonHandler>();
		_ = builder.Services.AddRawUpdateHandler<InlineHandler>();

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

	/// <summary>What each update's own DI scope contained, recorded from inside the handler.</summary>
	public sealed record ScopeRecord(
		string Key,
		int HolderId,
		int FeedbackId,
		int BoundUpdateId,
		bool CallbackAnsweredAtEnd);

	/// <summary>Collects handler activity plus per-update scope identities.</summary>
	public sealed class ScopeLog(Transcript transcript)
	{
		private readonly ConcurrentQueue<ScopeRecord> _scopes = [];
		private readonly ConcurrentQueue<string> _handled = [];

		public IReadOnlyList<ScopeRecord> Scopes => [.. _scopes];

		public IReadOnlyList<string> HandledKeys => [.. _handled];

		public IEnumerable<string> Bindings =>
			_scopes.Select(static s => string.Create(
				CultureInfo.InvariantCulture,
				$"{s.Key,-14} holder#{s.HolderId} feedback#{s.FeedbackId} update={s.BoundUpdateId} callbackAnswered={s.CallbackAnsweredAtEnd}"));

		public bool Handled(string key) => _handled.Contains(key, StringComparer.Ordinal);

		public void Record(string key, UpdateContextHolder holder, FeedbackService feedback)
		{
			_scopes.Enqueue(new ScopeRecord(
				key,
				RuntimeHelpers.GetHashCode(holder),
				RuntimeHelpers.GetHashCode(feedback),
				holder.Update.Id,
				feedback.CallbackAnswered));

			_handled.Enqueue(key);
		}

		public void Write(string line) => transcript.Write($"bot handler   {line}");
	}

	/// <summary>Replies to /menu using the injected <see cref="MessageContext"/>.</summary>
	public sealed class MenuHandler(
		MessageContext message,
		FeedbackService feedback,
		UpdateContextHolder holder,
		ScopeLog log) : IRawUpdateHandler
	{
		public async Task HandleAsync(Update update, CancellationToken cancellationToken)
		{
			log.Write($"MenuHandler   chat {message.ChatId} text \"{message.Message.Text}\"");

			var keyboard = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("Blue", "pick:blue"));
			_ = await feedback.SendWithKeyboardAsync("pick one", keyboard, cancellationToken: cancellationToken);

			log.Record($"message:{update.Id}", holder, feedback);
		}
	}

	/// <summary>Answers and edits using the injected <see cref="CallbackContext"/>.</summary>
	public sealed class ButtonHandler(
		CallbackContext callback,
		FeedbackService feedback,
		UpdateContextHolder holder,
		ScopeLog log) : IRawUpdateHandler
	{
		public async Task HandleAsync(Update update, CancellationToken cancellationToken)
		{
			log.Write($"ButtonHandler chat {callback.ChatId} data \"{callback.Data}\"");

			await feedback.AnswerCallbackAsync("Blue it is", cancellationToken: cancellationToken);

			// Double answer: a real handler often answers again on a later branch.
			await feedback.AnswerCallbackAsync("ignored second answer", cancellationToken: cancellationToken);

			await feedback.EditAsync("You picked blue", cancellationToken: cancellationToken);

			log.Record($"callback:{update.Id}", holder, feedback);
		}
	}

	/// <summary>Answers using the injected <see cref="InlineQueryContext"/>.</summary>
	public sealed class InlineHandler(
		InlineQueryContext inlineQuery,
		FeedbackService feedback,
		UpdateContextHolder holder,
		ScopeLog log) : IRawUpdateHandler
	{
		public async Task HandleAsync(Update update, CancellationToken cancellationToken)
		{
			log.Write($"InlineHandler user {inlineQuery.UserId} query \"{inlineQuery.Query}\"");

			await feedback.AnswerInlineAsync(
				[new InlineQueryResultArticle("r1", "Vexel", new InputTextMessageContent("Vexel v2"))],
				cancellationToken: cancellationToken);

			log.Record($"inline:{update.Id}", holder, feedback);
		}
	}
}
