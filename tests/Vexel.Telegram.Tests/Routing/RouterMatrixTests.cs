using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Vexel.Telegram.Handlers;
using Vexel.Telegram.Handlers.Contexts;
using Vexel.Telegram.Handlers.Routing;
using Vexel.Telegram.Tests.Fakes;

namespace Vexel.Telegram.Tests.Routing;

/// <summary>
/// T11 matrix: every routed kind + shared failure paths on the fake bot client, no network.
/// </summary>
public sealed class RouterMatrixTests
{
	[Theory]
	[InlineData("command")]
	[InlineData("callback")]
	[InlineData("inline")]
	[InlineData("chosen")]
	public async Task Binding_failure_logs_warning_and_does_not_throw(string kind)
	{
		var logger = new RecordingLogger<TelegramRouter>();
		var (contribution, update, routeLabel) = kind switch
		{
			"command" => (
				CommandContribution("add", static (_, _, _) => ValueTask.FromResult(false)),
				CommandUpdate("/add abc"),
				"/add"),
			"callback" => (
				CallbackContribution("go", static (_, _, _) => ValueTask.FromResult(false)),
				CallbackUpdate("go|x"),
				"callback"),
			"inline" => (
				InlineContribution("search", static (_, _, _) => ValueTask.FromResult(false)),
				InlineUpdate("search foo"),
				"inline query"),
			"chosen" => (
				ChosenContribution("item", static (_, _, _) => ValueTask.FromResult(false)),
				ChosenUpdate("item|1"),
				"chosen inline result"),
			_ => throw new ArgumentOutOfRangeException(nameof(kind)),
		};

		var bot = new RecordingTelegramBotClient { Username = "TestBot" };
		var router = new TelegramRouter([contribution], bot, logger);

		var ex = await Record.ExceptionAsync(() =>
			router.RouteAsync(update, new ServiceCollection().BuildServiceProvider(), CancellationToken.None));

		Assert.Null(ex);
		Assert.Contains(
			logger.Entries,
			e => e.Level == LogLevel.Warning
				&& e.Message.Contains("Failed to bind arguments", StringComparison.Ordinal)
				&& e.Message.Contains(routeLabel, StringComparison.OrdinalIgnoreCase));
	}

	[Theory]
	[InlineData("command")]
	[InlineData("callback")]
	[InlineData("inline")]
	[InlineData("chosen")]
	public async Task Handler_exception_is_isolated_per_kind(string kind)
	{
		var logger = new RecordingLogger<TelegramRouter>();
		var (contribution, update) = kind switch
		{
			"command" => (
				CommandContribution("boom", static (_, _, _) => throw new InvalidOperationException("x")),
				CommandUpdate("/boom")),
			"callback" => (
				CallbackContribution("boom", static (_, _, _) => throw new InvalidOperationException("x")),
				CallbackUpdate("boom")),
			"inline" => (
				InlineContribution("boom", static (_, _, _) => throw new InvalidOperationException("x")),
				InlineUpdate("boom")),
			"chosen" => (
				ChosenContribution("boom", static (_, _, _) => throw new InvalidOperationException("x")),
				ChosenUpdate("boom")),
			_ => throw new ArgumentOutOfRangeException(nameof(kind)),
		};

		var router = new TelegramRouter(
			[contribution],
			new RecordingTelegramBotClient { Username = "TestBot" },
			logger);

		var ex = await Record.ExceptionAsync(() =>
			router.RouteAsync(update, new ServiceCollection().BuildServiceProvider(), CancellationToken.None));

		Assert.Null(ex);
		Assert.Contains(logger.Entries, static e => e.Level == LogLevel.Error && e.Exception is not null);
	}

	[Fact]
	public async Task Callback_and_inline_misses_still_discharge_answer_obligations()
	{
		var bot = new RecordingTelegramBotClient();
		var callbackContribution = CallbackContribution("known", static (_, _, _) => ValueTask.FromResult(true));
		var inlineContribution = InlineContribution("known", static (_, _, _) => ValueTask.FromResult(true));
		var router = new TelegramRouter(
			[callbackContribution, inlineContribution],
			bot,
			NullLogger<TelegramRouter>.Instance);
		var callbackHook = new CallbackAnswerObligation(bot, NullLogger<CallbackAnswerObligation>.Instance);
		var inlineHook = new InlineAnswerObligation(bot, NullLogger<InlineAnswerObligation>.Instance);

		await using var provider = BuildFeedbackProvider(bot);

		var callbackUpdate = CallbackUpdate("ghost|1");
		using (var scope = provider.CreateScope())
		{
			scope.ServiceProvider.GetRequiredService<UpdateContextHolder>().Set(callbackUpdate);
			await router.RouteAsync(callbackUpdate, scope.ServiceProvider, CancellationToken.None);
			await callbackHook.CompleteAsync(callbackUpdate, scope.ServiceProvider, CancellationToken.None);
		}

		var inlineUpdate = InlineUpdate("ghost query");
		using (var scope = provider.CreateScope())
		{
			scope.ServiceProvider.GetRequiredService<UpdateContextHolder>().Set(inlineUpdate);
			await router.RouteAsync(inlineUpdate, scope.ServiceProvider, CancellationToken.None);
			await inlineHook.CompleteAsync(inlineUpdate, scope.ServiceProvider, CancellationToken.None);
		}

		_ = Assert.Single(bot.OfType<AnswerCallbackQueryRequest>());
		var inlineAnswer = Assert.Single(bot.OfType<AnswerInlineQueryRequest>());
		Assert.Empty(inlineAnswer.Results);
		Assert.Equal(0, inlineAnswer.CacheTime);
	}

	[Fact]
	public async Task Routed_hit_then_On_then_raw_order_is_stable_for_every_kind()
	{
		// Message / callback / inline / chosen share the same precedence: routed → On* (router) → raw
		// (dispatcher). This matrix covers the router half; raw is asserted in scheduler tests.
		foreach (var (kind, contribution, update, expected) in BuildKindOrderCases())
		{
			var order = new List<string>();
			var router = new TelegramRouter(
				[contribution(order)],
				new RecordingTelegramBotClient { Username = "TestBot" },
				NullLogger<TelegramRouter>.Instance);

			await router.RouteAsync(update, new ServiceCollection().BuildServiceProvider(), CancellationToken.None);

			Assert.True(
				expected.SequenceEqual(order, StringComparer.Ordinal),
				$"{kind}: expected [{string.Join(',', expected)}] got [{string.Join(',', order)}]");
		}
	}

	[Fact]
	public async Task Cancellation_during_routed_stage_does_not_run_On_handlers()
	{
		var onInvoked = false;
		using var cts = new CancellationTokenSource();

		var contribution = new TelegramRouteContribution(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase)
			{
				["ping"] = async (_, _, ct) =>
				{
					await cts.CancelAsync();
					ct.ThrowIfCancellationRequested();
					return true;
				},
			},
			commandMetadata: [],
			onMessages:
			[
				new OnHandlerEntry(
					"global::Demo.Observer",
					(_, _, _) =>
					{
						onInvoked = true;
						return ValueTask.FromResult(true);
					}),
			]);

		var router = new TelegramRouter(
			[contribution],
			new RecordingTelegramBotClient { Username = "TestBot" },
			NullLogger<TelegramRouter>.Instance);

		_ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			router.RouteAsync(
				CommandUpdate("/ping"),
				new ServiceCollection().BuildServiceProvider(),
				cts.Token));

		Assert.False(onInvoked);
	}

	[Fact]
	public async Task Caption_never_command_routes_even_with_caption_entities()
	{
		var invoked = new StrongBox<bool>(value: false);
		var contribution = CommandContribution(
			"ping",
			(_, _, _) =>
			{
				invoked.Value = true;
				return ValueTask.FromResult(true);
			});

		var router = new TelegramRouter(
			[contribution],
			new RecordingTelegramBotClient { Username = "TestBot" },
			NullLogger<TelegramRouter>.Instance);

		var update = new Update
		{
			Id = 1,
			Message = new Message
			{
				Id = 1,
				Date = DateTime.UtcNow,
				Chat = new Chat { Id = 7 },
				Caption = "/ping",
				CaptionEntities =
				[
					new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 0, Length = 5 },
				],
			},
		};

		await router.RouteAsync(update, new ServiceCollection().BuildServiceProvider(), CancellationToken.None);
		Assert.False(invoked.Value);
	}

	private static IEnumerable<(string Kind, Func<List<string>, TelegramRouteContribution> Contribution, Update Update, string[] Expected)>
		BuildKindOrderCases()
	{
		yield return (
			"command",
			order => new TelegramRouteContribution(
				"TestAsm",
				commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase)
				{
					["ping"] = Append("routed", order),
				},
				commandMetadata: [],
				onMessages: [new OnHandlerEntry("global::Demo.On", Append("on", order))]),
			CommandUpdate("/ping"),
			["routed", "on"]);

		yield return (
			"callback",
			order => new TelegramRouteContribution(
				"TestAsm",
				commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
				commandMetadata: [],
				callbacks: new Dictionary<string, RouteBinder>(StringComparer.Ordinal)
				{
					["ok"] = Append("routed", order),
				},
				onCallbackQueries: [new OnHandlerEntry("global::Demo.On", Append("on", order))]),
			CallbackUpdate("ok"),
			["routed", "on"]);

		yield return (
			"inline",
			order => new TelegramRouteContribution(
				"TestAsm",
				commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
				commandMetadata: [],
				inlineQueries: new Dictionary<string, RouteBinder>(StringComparer.Ordinal)
				{
					["search"] = Append("routed", order),
				},
				onInlineQueries: [new OnHandlerEntry("global::Demo.On", Append("on", order))]),
			InlineUpdate("search foo"),
			["routed", "on"]);

		yield return (
			"chosen",
			order => new TelegramRouteContribution(
				"TestAsm",
				commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
				commandMetadata: [],
				chosenInlineResults: new Dictionary<string, RouteBinder>(StringComparer.Ordinal)
				{
					["item"] = Append("routed", order),
				},
				onChosenInlineResults: [new OnHandlerEntry("global::Demo.On", Append("on", order))]),
			ChosenUpdate("item|1"),
			["routed", "on"]);
	}

	private static RouteBinder Append(string name, List<string> order) =>
		(_, _, _) =>
		{
			order.Add(name);
			return ValueTask.FromResult(true);
		};

	private static ServiceProvider BuildFeedbackProvider(ITelegramBotClient bot)
	{
		var services = new ServiceCollection();
		_ = services.AddSingleton(bot);
		_ = services.AddScoped<UpdateContextHolder>();
		_ = services.AddScoped<Feedback>();
		return services.BuildServiceProvider(validateScopes: true);
	}

	private static TelegramRouteContribution CommandContribution(string key, RouteBinder binder) =>
		new(
			"TestAsm",
			new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase) { [key] = binder },
			[]);

	private static TelegramRouteContribution CallbackContribution(string key, RouteBinder binder) =>
		new(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			callbacks: new Dictionary<string, RouteBinder>(StringComparer.Ordinal) { [key] = binder });

	private static TelegramRouteContribution InlineContribution(string key, RouteBinder binder) =>
		new(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			inlineQueries: new Dictionary<string, RouteBinder>(StringComparer.Ordinal) { [key] = binder });

	private static TelegramRouteContribution ChosenContribution(string key, RouteBinder binder) =>
		new(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			chosenInlineResults: new Dictionary<string, RouteBinder>(StringComparer.Ordinal) { [key] = binder });

	private static Update CommandUpdate(string text)
	{
		var slash = text.IndexOf(' ', StringComparison.Ordinal);
		var entityLength = slash < 0 ? text.Length : slash;
		return new Update
		{
			Id = 1,
			Message = new Message
			{
				Id = 1,
				Date = DateTime.UtcNow,
				Chat = new Chat { Id = 7 },
				From = new User { Id = 9, IsBot = false, FirstName = "u" },
				Text = text,
				Entities =
				[
					new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 0, Length = entityLength },
				],
			},
		};
	}

	private static Update CallbackUpdate(string? data) =>
		new()
		{
			Id = 2,
			CallbackQuery = new CallbackQuery
			{
				Id = "cb-1",
				From = new User { Id = 1, IsBot = false, FirstName = "u" },
				ChatInstance = "c",
				Data = data,
				Message = new Message
				{
					Id = 5,
					Date = DateTime.UtcNow,
					Chat = new Chat { Id = 10 },
				},
			},
		};

	private static Update InlineUpdate(string query) =>
		new()
		{
			Id = 3,
			InlineQuery = new InlineQuery
			{
				Id = "iq-1",
				From = new User { Id = 1, IsBot = false, FirstName = "u" },
				Query = query,
				Offset = string.Empty,
			},
		};

	private static Update ChosenUpdate(string resultId) =>
		new()
		{
			Id = 4,
			ChosenInlineResult = new ChosenInlineResult
			{
				ResultId = resultId,
				From = new User { Id = 1, IsBot = false, FirstName = "u" },
				Query = "q",
			},
		};
}
