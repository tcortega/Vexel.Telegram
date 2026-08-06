using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InlineQueryResults;
using Vexel.Telegram.Handlers;
using Vexel.Telegram.Handlers.Contexts;
using Vexel.Telegram.Handlers.Routing;
using Vexel.Telegram.Tests.Fakes;

namespace Vexel.Telegram.Tests.Routing;

public sealed class InlineRouterTests
{
	[Theory]
	[InlineData("search foo", "search", "foo")]
	[InlineData("search", "search", "")]
	[InlineData("search  bar baz", "search", "bar baz")]
	public async Task Trigger_routes_on_first_token_with_remainder_payload(
		string query,
		string expectedTrigger,
		string expectedPayload)
	{
		string? seenPayload = null;
		string? seenRoute = null;
		var contribution = InlineContribution(
			("search", (scope, payload, ct) =>
			{
				_ = scope;
				_ = ct;
				seenRoute = "search";
				seenPayload = payload;
				return ValueTask.FromResult(true);
			}
		),
			("", (scope, payload, ct) =>
			{
				_ = scope;
				_ = ct;
				seenRoute = "default";
				seenPayload = payload;
				return ValueTask.FromResult(true);
			}
		));

		await UsingPipelineAsync(
			[contribution],
			InlineUpdate(query),
			async (router, obligation, bot, services, update) =>
			{
				await router.RouteAsync(update, services, CancellationToken.None);
				await obligation.CompleteAsync(update, services, CancellationToken.None);

				Assert.Equal(expectedTrigger, seenRoute);
				Assert.Equal(expectedPayload, seenPayload);
				// B4: default empty answer because binder did not answer via Feedback.
				var answered = Assert.Single(bot.OfType<AnswerInlineQueryRequest>());
				Assert.Equal(0, answered.CacheTime);
				Assert.Empty(answered.Results);
			});
	}

	[Theory]
	[InlineData("")]
	[InlineData("hello world")]
	[InlineData("SEARCH foo")] // case-sensitive: SEARCH is not the "search" trigger
	public async Task Empty_or_unmatched_query_routes_to_default_with_full_query_text(string query)
	{
		string? seenPayload = null;
		var defaultInvoked = new StrongBox<bool>(value: false);
		var triggerInvoked = new StrongBox<bool>(value: false);
		var contribution = InlineContribution(
			("search", (_, _, _) =>
			{
				triggerInvoked.Value = true;
				return ValueTask.FromResult(true);
			}
		),
			("", (_, payload, _) =>
			{
				defaultInvoked.Value = true;
				seenPayload = payload;
				return ValueTask.FromResult(true);
			}
		));

		await UsingPipelineAsync(
			[contribution],
			InlineUpdate(query),
			async (router, obligation, bot, services, update) =>
			{
				await router.RouteAsync(update, services, CancellationToken.None);
				await obligation.CompleteAsync(update, services, CancellationToken.None);

				Assert.False(triggerInvoked.Value);
				Assert.True(defaultInvoked.Value);
				Assert.Equal(query, seenPayload);
				_ = Assert.Single(bot.OfType<AnswerInlineQueryRequest>());
			});
	}

	[Fact]
	public async Task Never_routes_on_Inline_query_id()
	{
		// Opaque Telegram server id that looks like a trigger must not select a route.
		string? seen = null;
		var contribution = InlineContribution(
			("999001", (_, payload, _) =>
			{
				seen = payload;
				return ValueTask.FromResult(true);
			}
		),
			("", (_, payload, _) =>
			{
				seen = "default:" + payload;
				return ValueTask.FromResult(true);
			}
		));

		var update = InlineUpdate(query: "hello", inlineQueryId: "999001");
		await UsingPipelineAsync(
			[contribution],
			update,
			async (router, obligation, _, services, u) =>
			{
				await router.RouteAsync(u, services, CancellationToken.None);
				await obligation.CompleteAsync(u, services, CancellationToken.None);

				Assert.Equal("default:hello", seen);
			});
	}

	[Fact]
	public async Task Throw_before_answer_sends_exactly_one_default_empty_answer()
	{
		var contribution = InlineContribution(
			("boom", static (_, _, _) => throw new InvalidOperationException("handler boom")));

		var update = InlineUpdate("boom");
		await UsingPipelineAsync(
			[contribution],
			update,
			async (router, obligation, bot, services, u) =>
			{
				await router.RouteAsync(u, services, CancellationToken.None);
				await obligation.CompleteAsync(u, services, CancellationToken.None);

				var answered = Assert.Single(bot.OfType<AnswerInlineQueryRequest>());
				Assert.Equal("iq-1", answered.InlineQueryId);
				Assert.Equal(0, answered.CacheTime);
				Assert.Empty(answered.Results);
			});
	}

	[Fact]
	public async Task Answer_then_throw_sends_no_second_answer()
	{
		var contribution = InlineContribution(
			("flip", static async (scope, _, ct) =>
			{
				var feedback = scope.GetRequiredService<Feedback>();
				await feedback.AnswerInlineAsync(
					[new InlineQueryResultArticle { Id = "1", Title = "t", InputMessageContent = new InputTextMessageContent("x") }],
					cacheTime: 30,
					cancellationToken: ct);
				throw new InvalidOperationException("after answer");
			}
		));

		var update = InlineUpdate("flip");
		await UsingPipelineAsync(
			[contribution],
			update,
			async (router, obligation, bot, services, u) =>
			{
				await router.RouteAsync(u, services, CancellationToken.None);
				await obligation.CompleteAsync(u, services, CancellationToken.None);

				var answered = Assert.Single(bot.OfType<AnswerInlineQueryRequest>());
				Assert.Equal(30, answered.CacheTime);
				_ = Assert.Single(answered.Results);
			});
	}

	[Fact]
	public async Task Unrouted_inline_query_is_answered_when_no_default_handler()
	{
		var invoked = new StrongBox<bool>(value: false);
		var contribution = InlineContribution(
			("known", (_, _, _) =>
			{
				invoked.Value = true;
				return ValueTask.FromResult(true);
			}
		));

		var update = InlineUpdate("unknown stuff");
		await UsingPipelineAsync(
			[contribution],
			update,
			async (router, obligation, bot, services, u) =>
			{
				await router.RouteAsync(u, services, CancellationToken.None);
				await obligation.CompleteAsync(u, services, CancellationToken.None);

				Assert.False(invoked.Value);
				var answered = Assert.Single(bot.OfType<AnswerInlineQueryRequest>());
				Assert.Equal(0, answered.CacheTime);
				Assert.Empty(answered.Results);
			});
	}

	[Fact]
	public async Task Successful_handler_answer_is_not_duplicated_by_obligation()
	{
		var contribution = InlineContribution(
			("ok", static async (scope, _, ct) =>
			{
				await scope.GetRequiredService<Feedback>().AnswerInlineAsync([], cacheTime: 5, cancellationToken: ct);
				return true;
			}
		));

		var update = InlineUpdate("ok");
		await UsingPipelineAsync(
			[contribution],
			update,
			async (router, obligation, bot, services, u) =>
			{
				await router.RouteAsync(u, services, CancellationToken.None);
				await obligation.CompleteAsync(u, services, CancellationToken.None);

				var answered = Assert.Single(bot.OfType<AnswerInlineQueryRequest>());
				Assert.Equal(5, answered.CacheTime);
			});
	}

	[Fact]
	public void Duplicate_cross_assembly_inline_keys_fail_fast()
	{
		static TelegramRouteContribution Make(string asm) => new(
			asm,
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			inlineQueries: new Dictionary<string, RouteBinder>(StringComparer.Ordinal)
			{
				["search"] = static (_, _, _) => ValueTask.FromResult(true),
			});

		var ex = Assert.Throws<InvalidOperationException>(() =>
			_ = new TelegramRouter(
				[Make("A"), Make("B")],
				new RecordingTelegramBotClient(),
				NullLogger<TelegramRouter>.Instance));

		Assert.Contains("Duplicate inline query route 'search'", ex.Message, StringComparison.Ordinal);
		Assert.Contains("'A'", ex.Message, StringComparison.Ordinal);
		Assert.Contains("'B'", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Duplicate_cross_assembly_default_inline_handlers_fail_fast()
	{
		static TelegramRouteContribution Make(string asm) => new(
			asm,
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			inlineQueries: new Dictionary<string, RouteBinder>(StringComparer.Ordinal)
			{
				[""] = static (_, _, _) => ValueTask.FromResult(true),
			});

		var ex = Assert.Throws<InvalidOperationException>(() =>
			_ = new TelegramRouter(
				[Make("A"), Make("B")],
				new RecordingTelegramBotClient(),
				NullLogger<TelegramRouter>.Instance));

		Assert.Contains("Duplicate inline query route '<default>'", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Inline_updates_do_not_warn_when_no_inline_routes_are_registered()
	{
		var logger = new RecordingLogger<TelegramRouter>();
		var bot = new RecordingTelegramBotClient();
		var contribution = new TelegramRouteContribution(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: []);
		var router = new TelegramRouter([contribution], bot, logger);

		var services = new ServiceCollection();
		await using var provider = services.BuildServiceProvider(validateScopes: true);
		await router.RouteAsync(InlineUpdate("raw"), provider, CancellationToken.None);

		Assert.Empty(logger.Entries);
	}

	[Fact]
	public async Task Already_answered_rejection_of_default_answer_is_not_a_warning()
	{
		var logger = new RecordingLogger<AnswerObligation>();
		var bot = new RecordingTelegramBotClient
		{
			FailRequest = static request => request is AnswerInlineQueryRequest
				? new ApiRequestException(
					"Bad Request: query is too old and response timeout expired or query ID is invalid",
					400)
				: null,
		};
		var obligation = new AnswerObligation(bot, logger);

		var services = new ServiceCollection();
		_ = services.AddSingleton<ITelegramBotClient>(bot);
		_ = services.AddScoped<UpdateContextHolder>();
		_ = services.AddScoped<Feedback>();
		await using var provider = services.BuildServiceProvider(validateScopes: true);
		using var scope = provider.CreateScope();
		var update = InlineUpdate("gone");
		scope.ServiceProvider.GetRequiredService<UpdateContextHolder>().Set(update);

		await obligation.CompleteAsync(update, scope.ServiceProvider, CancellationToken.None);

		_ = Assert.Single(bot.OfType<AnswerInlineQueryRequest>());
		Assert.All(logger.Entries, static e => Assert.True(e.Level < LogLevel.Warning));
	}

	[Fact]
	public async Task Chosen_inline_result_routes_on_result_id_prefix()
	{
		string? seenPayload = null;
		var contribution = ChosenContribution(
			"item",
			(_, payload, _) =>
			{
				seenPayload = payload;
				return ValueTask.FromResult(true);
			});

		var router = new TelegramRouter(
			[contribution],
			new RecordingTelegramBotClient(),
			NullLogger<TelegramRouter>.Instance);

		var services = new ServiceCollection();
		await using var provider = services.BuildServiceProvider(validateScopes: true);
		await router.RouteAsync(ChosenUpdate("item|42"), provider, CancellationToken.None);

		Assert.Equal("42", seenPayload);
	}

	[Fact]
	public async Task Chosen_inline_result_without_suffix_binds_empty_string()
	{
		string? seenPayload = null;
		var contribution = ChosenContribution(
			"item",
			(_, payload, _) =>
			{
				seenPayload = payload;
				return ValueTask.FromResult(true);
			});

		var router = new TelegramRouter(
			[contribution],
			new RecordingTelegramBotClient(),
			NullLogger<TelegramRouter>.Instance);

		var services = new ServiceCollection();
		await using var provider = services.BuildServiceProvider(validateScopes: true);
		await router.RouteAsync(ChosenUpdate("item"), provider, CancellationToken.None);

		Assert.Equal(string.Empty, seenPayload);
	}

	[Fact]
	public void Duplicate_cross_assembly_chosen_keys_fail_fast()
	{
		static TelegramRouteContribution Make(string asm) => new(
			asm,
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			chosenInlineResults: new Dictionary<string, RouteBinder>(StringComparer.Ordinal)
			{
				["item"] = static (_, _, _) => ValueTask.FromResult(true),
			});

		var ex = Assert.Throws<InvalidOperationException>(() =>
			_ = new TelegramRouter(
				[Make("A"), Make("B")],
				new RecordingTelegramBotClient(),
				NullLogger<TelegramRouter>.Instance));

		Assert.Contains("Duplicate chosen inline result route 'item'", ex.Message, StringComparison.Ordinal);
	}

	private static async Task UsingPipelineAsync(
		IEnumerable<TelegramRouteContribution> contributions,
		Update update,
		Func<TelegramRouter, AnswerObligation, RecordingTelegramBotClient, IServiceProvider, Update, Task> body)
	{
		var bot = new RecordingTelegramBotClient();
		var router = new TelegramRouter(contributions, bot, NullLogger<TelegramRouter>.Instance);
		var obligation = new AnswerObligation(bot, NullLogger<AnswerObligation>.Instance);

		var services = new ServiceCollection();
		_ = services.AddSingleton<ITelegramBotClient>(bot);
		_ = services.AddScoped<UpdateContextHolder>();
		_ = services.AddScoped<Feedback>();
		await using var provider = services.BuildServiceProvider(validateScopes: true);
		using var scope = provider.CreateScope();
		scope.ServiceProvider.GetRequiredService<UpdateContextHolder>().Set(update);
		await body(router, obligation, bot, scope.ServiceProvider, update);
	}

	private static TelegramRouteContribution InlineContribution(params (string Key, RouteBinder Binder)[] routes)
	{
		var map = new Dictionary<string, RouteBinder>(StringComparer.Ordinal);
		foreach (var (key, binder) in routes)
		{
			map[key] = binder;
		}

		return new TelegramRouteContribution(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			inlineQueries: map);
	}

	private static TelegramRouteContribution ChosenContribution(string key, RouteBinder binder) =>
		new(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			chosenInlineResults: new Dictionary<string, RouteBinder>(StringComparer.Ordinal)
			{
				[key] = binder,
			});

	private static Update InlineUpdate(string query, string inlineQueryId = "iq-1") =>
		new()
		{
			Id = 11,
			InlineQuery = new InlineQuery
			{
				Id = inlineQueryId,
				From = new User { Id = 1, IsBot = false, FirstName = "u" },
				Query = query,
				Offset = string.Empty,
			},
		};

	private static Update ChosenUpdate(string resultId) =>
		new()
		{
			Id = 12,
			ChosenInlineResult = new ChosenInlineResult
			{
				ResultId = resultId,
				From = new User { Id = 1, IsBot = false, FirstName = "u" },
				Query = "q",
			},
		};
}
