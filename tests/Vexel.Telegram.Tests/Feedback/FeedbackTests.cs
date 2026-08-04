using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Vexel.Telegram.Client;
using Vexel.Telegram.Client.Dispatch;
using Vexel.Telegram.Client.Extensions;
using Vexel.Telegram.Handlers.Contexts;
using Vexel.Telegram.Handlers.DependencyInjection;
using Vexel.Telegram.Tests.Fakes;
using FeedbackService = Vexel.Telegram.Handlers.Feedback;

namespace Vexel.Telegram.Tests.Handlers;

public sealed class FeedbackTests
{
	[Fact]
	public async Task ReplyAsync_SendsToContextualChat()
	{
		var bot = new RecordingTelegramBotClient();
		var holder = new UpdateContextHolder();
		holder.Set(MessageUpdate(1, chatId: 55));
		var feedback = new FeedbackService(bot, holder);

		_ = await feedback.ReplyAsync("hello");

		var sent = Assert.Single(bot.OfType<SendMessageRequest>());
		Assert.Equal(55, sent.ChatId.Identifier);
		Assert.Equal("hello", sent.Text);
	}

	[Fact]
	public async Task AnswerCallbackAsync_IsIdempotent_FirstWins()
	{
		var bot = new RecordingTelegramBotClient();
		var holder = new UpdateContextHolder();
		holder.Set(CallbackUpdate("cb-id", chatId: 10));
		var feedback = new FeedbackService(bot, holder);

		await feedback.AnswerCallbackAsync("first");
		await feedback.AnswerCallbackAsync("second");
		await feedback.AnswerCallbackAsync("third");

		Assert.True(feedback.CallbackAnswered);
		var answered = Assert.Single(bot.OfType<AnswerCallbackQueryRequest>());
		Assert.Equal("cb-id", answered.CallbackQueryId);
		Assert.Equal("first", answered.Text);
	}

	[Fact]
	public async Task AnswerInlineAsync_IsIdempotent_FirstWins()
	{
		var bot = new RecordingTelegramBotClient();
		var holder = new UpdateContextHolder();
		holder.Set(InlineQueryUpdate("iq-id"));
		var feedback = new FeedbackService(bot, holder);

		await feedback.AnswerInlineAsync([]);
		await feedback.AnswerInlineAsync([]);

		Assert.True(feedback.InlineAnswered);
		var answered = Assert.Single(bot.OfType<AnswerInlineQueryRequest>());
		Assert.Equal("iq-id", answered.InlineQueryId);
	}

	[Fact]
	public async Task SendWithKeyboardAsync_AttachesMarkup()
	{
		var bot = new RecordingTelegramBotClient();
		var holder = new UpdateContextHolder();
		holder.Set(MessageUpdate(1, chatId: 3));
		var feedback = new FeedbackService(bot, holder);
		var keyboard = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("Go", "go"));

		_ = await feedback.SendWithKeyboardAsync("pick", keyboard);

		var sent = Assert.Single(bot.OfType<SendMessageRequest>());
		Assert.Same(keyboard, sent.ReplyMarkup);
		Assert.Equal("pick", sent.Text);
	}

	[Fact]
	public async Task B1_ConcurrentChats_FeedbackBoundToOwnChat_NoLeakage()
	{
		var bot = new RecordingTelegramBotClient();
		var chatIds = new ConcurrentBag<long>();
		var bothStarted = new BothStartedGate();
		var release = new ReleaseGate();

		var services = new ServiceCollection();
		_ = services.AddSingleton<ITelegramBotClient>(bot);
		_ = services.AddSingleton(chatIds);
		_ = services.AddSingleton(bothStarted);
		_ = services.AddSingleton(release);
		_ = services.AddSingleton(new StartCounter());
		_ = services.AddLogging(static b => b.ClearProviders());
		_ = services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
		_ = services.AddTelegramBot(static _ => "test-token");
		_ = services.AddRawUpdateHandler<IsolationRawHandler>();

		await using var provider = services.BuildServiceProvider(validateScopes: true);
		var dispatcher = provider.GetRequiredService<IUpdateDispatcher>();

		var dispatch1 = dispatcher.DispatchAsync(MessageUpdate(1, chatId: 100), CancellationToken.None);
		var dispatch2 = dispatcher.DispatchAsync(MessageUpdate(2, chatId: 200), CancellationToken.None);

		await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		_ = release.TrySetResult();
		await Task.WhenAll(dispatch1, dispatch2);

		Assert.Equal([100L, 200L], [.. chatIds.Order()]);

		var sentChats = bot.OfType<SendMessageRequest>()
			.Select(static r => r.ChatId.Identifier!.Value)
			.Order()
			.ToArray();
		Assert.Equal([100L, 200L], sentChats);
	}

	[Fact]
	public void Holder_GetOutsideScope_ViaDiFactory_ThrowsClearMessage()
	{
		var services = new ServiceCollection();
		_ = services.AddSingleton<ITelegramBotClient>(new RecordingTelegramBotClient());
		_ = services.AddTelegramBot(static _ => "test-token");

		using var provider = services.BuildServiceProvider(validateScopes: true);
		using var scope = provider.CreateScope();

		// Holder is resolved but never Set (dispatcher did not run).
		var ex = Assert.Throws<InvalidOperationException>(
			() => scope.ServiceProvider.GetRequiredService<MessageContext>());

		Assert.Contains("resolved outside a Vexel update scope", ex.Message, StringComparison.Ordinal);
	}

	private static Update MessageUpdate(int id, long chatId) =>
		new()
		{
			Id = id,
			Message = new Message
			{
				Id = id,
				Chat = new Chat { Id = chatId },
				Date = DateTime.UtcNow,
				Text = $"m-{id}",
				From = new User { Id = chatId, IsBot = false, FirstName = "u" },
			},
		};

	private static Update CallbackUpdate(string callbackId, long chatId) =>
		new()
		{
			Id = 1,
			CallbackQuery = new CallbackQuery
			{
				Id = callbackId,
				From = new User { Id = 1, IsBot = false, FirstName = "u" },
				ChatInstance = "c",
				Data = "d",
				Message = new Message
				{
					Id = 5,
					Date = DateTime.UtcNow,
					Chat = new Chat { Id = chatId },
				},
			},
		};

	private static Update InlineQueryUpdate(string inlineQueryId) =>
		new()
		{
			Id = 1,
			InlineQuery = new InlineQuery
			{
				Id = inlineQueryId,
				From = new User { Id = 1, IsBot = false, FirstName = "u" },
				Query = "q",
				Offset = "",
			},
		};

	private sealed class StartCounter
	{
		private int _value;

		public int Increment() => Interlocked.Increment(ref _value);
	}

	private sealed class BothStartedGate : TaskCompletionSource
	{
		public BothStartedGate() : base(TaskCreationOptions.RunContinuationsAsynchronously)
		{
		}
	}

	private sealed class ReleaseGate : TaskCompletionSource
	{
		public ReleaseGate() : base(TaskCreationOptions.RunContinuationsAsynchronously)
		{
		}
	}

	private sealed class IsolationRawHandler(
		FeedbackService feedback,
		MessageContext message,
		ConcurrentBag<long> chatIds,
		StartCounter startCounter,
		BothStartedGate bothStarted,
		ReleaseGate release) : IRawUpdateHandler
	{
		public async Task HandleAsync(Update update, CancellationToken cancellationToken)
		{
			chatIds.Add(message.ChatId);

			if (startCounter.Increment() == 2)
			{
				_ = bothStarted.TrySetResult();
			}

			await release.Task.WaitAsync(cancellationToken);
			_ = await feedback.ReplyAsync($"from-{message.ChatId}", cancellationToken: cancellationToken);
		}
	}
}
