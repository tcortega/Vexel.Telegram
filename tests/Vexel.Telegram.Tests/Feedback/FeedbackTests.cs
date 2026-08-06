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
	public async Task AnswerCallbackAsync_FailedSend_DoesNotLatch_AndRetrySucceeds()
	{
		var bot = new RecordingTelegramBotClient
		{
			FailRequest = static request =>
				request is AnswerCallbackQueryRequest { Text: "first" }
					? new InvalidOperationException("transient")
					: null,
		};

		var holder = new UpdateContextHolder();
		holder.Set(CallbackUpdate("cb-id", chatId: 10));
		var feedback = new FeedbackService(bot, holder);

		_ = await Assert.ThrowsAsync<InvalidOperationException>(() => feedback.AnswerCallbackAsync("first"));
		Assert.False(feedback.CallbackAnswered);

		await feedback.AnswerCallbackAsync("second");

		Assert.True(feedback.CallbackAnswered);
		string[] answeredTexts = [.. bot.OfType<AnswerCallbackQueryRequest>().Select(static r => r.Text ?? "")];
		Assert.Equal(["first", "second"], answeredTexts);
	}

	[Fact]
	public async Task AnswerInlineAsync_FailedSend_DoesNotLatch_AndRetrySucceeds()
	{
		var attempts = 0;
		var bot = new RecordingTelegramBotClient
		{
			FailRequest = request =>
				request is AnswerInlineQueryRequest && ++attempts == 1
					? new InvalidOperationException("transient")
					: null,
		};

		var holder = new UpdateContextHolder();
		holder.Set(InlineQueryUpdate("iq-id"));
		var feedback = new FeedbackService(bot, holder);

		_ = await Assert.ThrowsAsync<InvalidOperationException>(() => feedback.AnswerInlineAsync([]));
		Assert.False(feedback.InlineAnswered);

		await feedback.AnswerInlineAsync([]);

		Assert.True(feedback.InlineAnswered);
		Assert.Equal(2, bot.OfType<AnswerInlineQueryRequest>().Count);
	}

	[Fact]
	public async Task EditAsync_MessageContext_ThrowsWithoutCallingBot()
	{
		var bot = new RecordingTelegramBotClient();
		var holder = new UpdateContextHolder();
		holder.Set(MessageUpdate(1, chatId: 55));
		var feedback = new FeedbackService(bot, holder);

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => feedback.EditAsync("edited"));

		Assert.Contains("no editable bot message", ex.Message, StringComparison.Ordinal);
		Assert.Empty(bot.Requests);
	}

	[Fact]
	public async Task EditAsync_CallbackContext_EditsTheBotMessage()
	{
		var bot = new RecordingTelegramBotClient();
		var holder = new UpdateContextHolder();
		holder.Set(CallbackUpdate("cb-id", chatId: 10));
		var feedback = new FeedbackService(bot, holder);

		await feedback.EditAsync("edited");

		var edit = Assert.Single(bot.OfType<EditMessageTextRequest>());
		Assert.Equal(10, edit.ChatId.Identifier);
		Assert.Equal(5, edit.MessageId);
		Assert.Equal("edited", edit.Text);
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
		var dispatcher = provider.GetRequiredService<UpdateDispatcher>();

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
			scope.ServiceProvider.GetRequiredService<MessageContext>);

		Assert.Contains("resolved outside a Vexel update scope", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void AddTelegramBot_ComposesWithAppRegisteredScopeInitializer()
	{
		var services = new ServiceCollection();
		_ = services.AddSingleton<ITelegramBotClient>(new RecordingTelegramBotClient());
		_ = services.AddScoped<IUpdateScopeInitializer, NoopScopeInitializer>();
		_ = services.AddTelegramBot(static _ => "test-token");

		using var provider = services.BuildServiceProvider(validateScopes: true);
		using var scope = provider.CreateScope();

		var initializers = scope.ServiceProvider.GetServices<IUpdateScopeInitializer>().ToArray();

		Assert.Contains(initializers, static i => i is UpdateContextScopeInitializer);
		Assert.Contains(initializers, static i => i is NoopScopeInitializer);
	}

	[Fact]
	public async Task Dispatch_WhenScopeInitializerCannotResolve_AbortsUpdate()
	{
		var ran = new ConcurrentBag<string>();
		var services = new ServiceCollection();
		_ = services.AddSingleton<ITelegramBotClient>(new RecordingTelegramBotClient());
		_ = services.AddSingleton(ran);
		_ = services.AddLogging(static b => b.ClearProviders());
		_ = services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
		_ = services.AddTelegramBot(static _ => "test-token");
		_ = services.AddScoped<IUpdateScopeInitializer, ThrowingConstructorScopeInitializer>();
		_ = services.AddRawUpdateHandler<AnyUpdateRawHandler>();

		await using var provider = services.BuildServiceProvider(validateScopes: true);
		var dispatcher = provider.GetRequiredService<UpdateDispatcher>();

		_ = await Assert.ThrowsAsync<InvalidOperationException>(
			() => dispatcher.DispatchAsync(MessageUpdate(1, chatId: 9), CancellationToken.None));

		Assert.Empty(ran);
	}

	[Fact]
	public async Task RawHandlers_WrongKindContext_DoesNotSkipSiblingHandlers()
	{
		var ran = new ConcurrentBag<string>();
		var services = new ServiceCollection();
		_ = services.AddSingleton<ITelegramBotClient>(new RecordingTelegramBotClient());
		_ = services.AddSingleton(ran);
		_ = services.AddLogging(static b => b.ClearProviders());
		_ = services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
		_ = services.AddTelegramBot(static _ => "test-token");
		_ = services.AddRawUpdateHandler<CallbackOnlyRawHandler>();
		_ = services.AddRawUpdateHandler<AnyUpdateRawHandler>();

		await using var provider = services.BuildServiceProvider(validateScopes: true);
		var dispatcher = provider.GetRequiredService<UpdateDispatcher>();

		await dispatcher.DispatchAsync(MessageUpdate(1, chatId: 7), CancellationToken.None);

		Assert.Equal(["any"], [.. ran]);
	}

	[Fact]
	public async Task RawHandlers_RunsHealthySiblingWhenAnotherFailsToResolve()
	{
		var tracker = new HandlerLifetimeTracker();
		var services = new ServiceCollection();
		_ = services.AddSingleton<ITelegramBotClient>(new RecordingTelegramBotClient());
		_ = services.AddSingleton(tracker);
		_ = services.AddSingleton(new ConcurrentBag<string>());
		_ = services.AddLogging(static b => b.ClearProviders());
		_ = services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
		_ = services.AddTelegramBot(static _ => "test-token");
		_ = services.AddRawUpdateHandler<LifetimeTrackedRawHandler>();
		_ = services.AddRawUpdateHandler<CallbackOnlyRawHandler>();

		await using var provider = services.BuildServiceProvider(validateScopes: true);
		var dispatcher = provider.GetRequiredService<UpdateDispatcher>();

		await dispatcher.DispatchAsync(MessageUpdate(1, chatId: 7), CancellationToken.None);

		Assert.Equal(1, tracker.Handled);
		Assert.Equal(tracker.Created, tracker.Disposed);
	}

	[Fact]
	public async Task RawHandlers_SingletonRegistration_IsNotRebuiltPerUpdate()
	{
		var tracker = new HandlerLifetimeTracker();
		var services = new ServiceCollection();
		_ = services.AddSingleton<ITelegramBotClient>(new RecordingTelegramBotClient());
		_ = services.AddSingleton(tracker);
		_ = services.AddSingleton(new ConcurrentBag<string>());
		_ = services.AddLogging(static b => b.ClearProviders());
		_ = services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
		_ = services.AddTelegramBot(static _ => "test-token");
		_ = services.AddRawUpdateHandler<LifetimeTrackedRawHandler>(ServiceLifetime.Singleton);
		_ = services.AddRawUpdateHandler<CallbackOnlyRawHandler>();

		var provider = services.BuildServiceProvider(validateScopes: true);
		await using (provider.ConfigureAwait(false))
		{
			var dispatcher = provider.GetRequiredService<UpdateDispatcher>();

			await dispatcher.DispatchAsync(MessageUpdate(1, chatId: 7), CancellationToken.None);
			var createdAfterFirstUpdate = tracker.Created;

			await dispatcher.DispatchAsync(MessageUpdate(2, chatId: 7), CancellationToken.None);
			await dispatcher.DispatchAsync(MessageUpdate(3, chatId: 7), CancellationToken.None);

			Assert.Equal(createdAfterFirstUpdate, tracker.Created);
			Assert.Equal(3, tracker.Handled);
			Assert.Equal(0, tracker.Disposed);
		}

		Assert.Equal(tracker.Created, tracker.Disposed);
	}

	[Fact]
	public async Task RawHandlers_SingletonWithScopedDependency_FailsResolution()
	{
		var chatIds = new ConcurrentBag<long>();
		var services = new ServiceCollection();
		_ = services.AddSingleton<ITelegramBotClient>(new RecordingTelegramBotClient());
		_ = services.AddSingleton(chatIds);
		_ = services.AddLogging(static b => b.ClearProviders());
		_ = services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
		_ = services.AddTelegramBot(static _ => "test-token");
		_ = services.AddRawUpdateHandler<MessageBoundSingletonRawHandler>(ServiceLifetime.Singleton);

		await using var provider = services.BuildServiceProvider(validateScopes: true);
		var dispatcher = provider.GetRequiredService<UpdateDispatcher>();

		await dispatcher.DispatchAsync(MessageUpdate(1, chatId: 100), CancellationToken.None);
		await dispatcher.DispatchAsync(MessageUpdate(2, chatId: 200), CancellationToken.None);

		Assert.Empty(chatIds);
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

	private sealed class NoopScopeInitializer : IUpdateScopeInitializer
	{
		public void Initialize(Update update)
		{
		}
	}

	private sealed class ThrowingConstructorScopeInitializer : IUpdateScopeInitializer
	{
		public ThrowingConstructorScopeInitializer() =>
			throw new InvalidOperationException("initializer construction boom");

		public void Initialize(Update update)
		{
		}
	}

	private sealed class AnyUpdateRawHandler(ConcurrentBag<string> ran) : IRawUpdateHandler
	{
		public Task HandleAsync(Update update, CancellationToken cancellationToken)
		{
			ran.Add("any");
			return Task.CompletedTask;
		}
	}

	private sealed class CallbackOnlyRawHandler(ConcurrentBag<string> ran, CallbackContext callback)
		: IRawUpdateHandler
	{
		public Task HandleAsync(Update update, CancellationToken cancellationToken)
		{
			ran.Add(callback.Id);
			return Task.CompletedTask;
		}
	}

	private sealed class MessageBoundSingletonRawHandler(ConcurrentBag<long> chatIds, MessageContext message)
		: IRawUpdateHandler
	{
		public Task HandleAsync(Update update, CancellationToken cancellationToken)
		{
			chatIds.Add(message.ChatId);
			return Task.CompletedTask;
		}
	}

	private sealed class HandlerLifetimeTracker
	{
		private int _created;
		private int _handled;
		private int _disposed;

		public int Created => Volatile.Read(ref _created);

		public int Handled => Volatile.Read(ref _handled);

		public int Disposed => Volatile.Read(ref _disposed);

		public void MarkCreated() => _ = Interlocked.Increment(ref _created);

		public void MarkHandled() => _ = Interlocked.Increment(ref _handled);

		public void MarkDisposed() => _ = Interlocked.Increment(ref _disposed);
	}

	private sealed class LifetimeTrackedRawHandler : IRawUpdateHandler, IDisposable
	{
		private readonly HandlerLifetimeTracker _tracker;

		public LifetimeTrackedRawHandler(HandlerLifetimeTracker tracker)
		{
			_tracker = tracker;
			tracker.MarkCreated();
		}

		public Task HandleAsync(Update update, CancellationToken cancellationToken)
		{
			_tracker.MarkHandled();
			return Task.CompletedTask;
		}

		public void Dispose() => _tracker.MarkDisposed();
	}

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
