using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Vexel.Telegram.Handlers;
using Vexel.Telegram.Handlers.Contexts;
using Vexel.Telegram.Handlers.Routing;
using Vexel.Telegram.Tests.Fakes;

namespace Vexel.Telegram.Tests.Routing;

public sealed class CallbackRouterTests
{
	[Fact]
	public async Task Routes_callback_key_and_suffix_to_binder()
	{
		string? seenPayload = null;
		var contribution = CallbackContribution(
			"confirm",
			(scope, payload, ct) =>
			{
				_ = scope;
				_ = ct;
				seenPayload = payload;
				return ValueTask.FromResult(true);
			});

		await UsingPipelineAsync(
			[contribution],
			CallbackUpdate("confirm|42"),
			async (router, obligation, bot, services, update) =>
			{
				await router.RouteAsync(update, services, CancellationToken.None);
				await obligation.CompleteAsync(update, services, CancellationToken.None);

				Assert.Equal("42", seenPayload);
				// B4: default answer because binder did not answer via Feedback.
				_ = Assert.Single(bot.OfType<AnswerCallbackQueryRequest>());
			});
	}

	[Fact]
	public async Task Throw_before_answer_sends_exactly_one_default_answer()
	{
		var contribution = CallbackContribution(
			"boom",
			static (_, _, _) => throw new InvalidOperationException("handler boom"));

		var update = CallbackUpdate("boom");
		await UsingPipelineAsync(
			[contribution],
			update,
			async (router, obligation, bot, services, u) =>
			{
				await router.RouteAsync(u, services, CancellationToken.None);
				await obligation.CompleteAsync(u, services, CancellationToken.None);

				var answered = Assert.Single(bot.OfType<AnswerCallbackQueryRequest>());
				Assert.Equal("cb-1", answered.CallbackQueryId);
				Assert.Null(answered.Text);
			});
	}

	[Fact]
	public async Task Answer_then_throw_sends_no_second_answer()
	{
		var contribution = CallbackContribution(
			"flip",
			static async (scope, _, ct) =>
			{
				var feedback = scope.GetRequiredService<Feedback>();
				await feedback.AnswerCallbackAsync("done", cancellationToken: ct);
				throw new InvalidOperationException("after answer");
			});

		var update = CallbackUpdate("flip");
		await UsingPipelineAsync(
			[contribution],
			update,
			async (router, obligation, bot, services, u) =>
			{
				await router.RouteAsync(u, services, CancellationToken.None);
				await obligation.CompleteAsync(u, services, CancellationToken.None);

				var answered = Assert.Single(bot.OfType<AnswerCallbackQueryRequest>());
				Assert.Equal("done", answered.Text);
			});
	}

	[Fact]
	public async Task Unrouted_callback_is_answered_and_does_not_invoke_other_binders()
	{
		var invoked = new StrongBox<bool>(value: false);
		var contribution = CallbackContribution(
			"known",
			(_, _, _) =>
			{
				invoked.Value = true;
				return ValueTask.FromResult(true);
			});

		var update = CallbackUpdate("unknown|x");
		await UsingPipelineAsync(
			[contribution],
			update,
			async (router, obligation, bot, services, u) =>
			{
				await router.RouteAsync(u, services, CancellationToken.None);
				await obligation.CompleteAsync(u, services, CancellationToken.None);

				Assert.False(invoked.Value);
				_ = Assert.Single(bot.OfType<AnswerCallbackQueryRequest>());
			});
	}

	[Fact]
	public async Task Successful_handler_answer_is_not_duplicated_by_obligation()
	{
		var contribution = CallbackContribution(
			"ok",
			static async (scope, _, ct) =>
			{
				await scope.GetRequiredService<Feedback>().AnswerCallbackAsync("ack", cancellationToken: ct);
				return true;
			});

		var update = CallbackUpdate("ok");
		await UsingPipelineAsync(
			[contribution],
			update,
			async (router, obligation, bot, services, u) =>
			{
				await router.RouteAsync(u, services, CancellationToken.None);
				await obligation.CompleteAsync(u, services, CancellationToken.None);

				var answered = Assert.Single(bot.OfType<AnswerCallbackQueryRequest>());
				Assert.Equal("ack", answered.Text);
			});
	}

	[Fact]
	public async Task Empty_record_callback_ignores_suffix()
	{
		var invoked = new StrongBox<bool>(value: false);
		var contribution = CallbackContribution(
			"ping",
			(_, payload, _) =>
			{
				// Empty-record binders still receive the suffix payload but ignore it.
				Assert.Equal("ignored", payload);
				invoked.Value = true;
				return ValueTask.FromResult(true);
			});

		var update = CallbackUpdate("ping|ignored");
		await UsingPipelineAsync(
			[contribution],
			update,
			async (router, obligation, _, services, u) =>
			{
				await router.RouteAsync(u, services, CancellationToken.None);
				await obligation.CompleteAsync(u, services, CancellationToken.None);
				Assert.True(invoked.Value);
			});
	}

	[Fact]
	public void Duplicate_cross_assembly_callback_keys_fail_fast()
	{
		static TelegramRouteContribution Make(string asm) => new(
			asm,
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			callbacks: new Dictionary<string, RouteBinder>(StringComparer.Ordinal)
			{
				["go"] = static (_, _, _) => ValueTask.FromResult(true),
			});

		var ex = Assert.Throws<InvalidOperationException>(() =>
			_ = new TelegramRouter(
				[Make("A"), Make("B")],
				new RecordingTelegramBotClient(),
				NullLogger<TelegramRouter>.Instance));

		Assert.Contains("Duplicate callback route 'go'", ex.Message, StringComparison.Ordinal);
		Assert.Contains("'A'", ex.Message, StringComparison.Ordinal);
		Assert.Contains("'B'", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Null_callback_data_is_unrouted_but_answered()
	{
		var contribution = CallbackContribution(
			"go",
			static (_, _, _) => ValueTask.FromResult(true));

		var update = CallbackUpdate(data: null);
		await UsingPipelineAsync(
			[contribution],
			update,
			async (router, obligation, bot, services, u) =>
			{
				await router.RouteAsync(u, services, CancellationToken.None);
				await obligation.CompleteAsync(u, services, CancellationToken.None);
				_ = Assert.Single(bot.OfType<AnswerCallbackQueryRequest>());
			});
	}

	[Fact]
	public async Task Callback_updates_do_not_warn_when_no_callback_routes_are_registered()
	{
		var logger = new RecordingLogger<TelegramRouter>();
		var bot = new RecordingTelegramBotClient();
		var contribution = new TelegramRouteContribution(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			callbacks: new Dictionary<string, RouteBinder>(StringComparer.Ordinal));
		var router = new TelegramRouter([contribution], bot, logger);

		var services = new ServiceCollection();
		await using var provider = services.BuildServiceProvider(validateScopes: true);
		await router.RouteAsync(CallbackUpdate("raw|handled"), provider, CancellationToken.None);

		Assert.Empty(logger.Entries);
	}

	[Fact]
	public async Task Unmatched_callback_key_logs_below_warning()
	{
		var logger = new RecordingLogger<TelegramRouter>();
		var bot = new RecordingTelegramBotClient();
		var router = new TelegramRouter(
			[CallbackContribution("known", static (_, _, _) => ValueTask.FromResult(true))],
			bot,
			logger);

		var services = new ServiceCollection();
		await using var provider = services.BuildServiceProvider(validateScopes: true);
		await router.RouteAsync(CallbackUpdate("unknown"), provider, CancellationToken.None);

		Assert.All(logger.Entries, static e => Assert.True(e.Level < LogLevel.Warning));
	}

	[Fact]
	public async Task Already_answered_rejection_of_default_answer_is_not_a_warning()
	{
		var logger = new RecordingLogger<CallbackAnswerObligation>();
		var bot = new RecordingTelegramBotClient
		{
			FailRequest = static request => request is AnswerCallbackQueryRequest
				? new ApiRequestException(
					"Bad Request: query is too old and response timeout expired or query ID is invalid",
					400)
				: null,
		};
		var obligation = new CallbackAnswerObligation(bot, logger);

		var services = new ServiceCollection();
		_ = services.AddSingleton<ITelegramBotClient>(bot);
		_ = services.AddScoped<UpdateContextHolder>();
		_ = services.AddScoped<Feedback>();
		await using var provider = services.BuildServiceProvider(validateScopes: true);
		using var scope = provider.CreateScope();
		var update = CallbackUpdate("gone");
		scope.ServiceProvider.GetRequiredService<UpdateContextHolder>().Set(update);

		await obligation.CompleteAsync(update, scope.ServiceProvider, CancellationToken.None);

		_ = Assert.Single(bot.OfType<AnswerCallbackQueryRequest>());
		Assert.All(logger.Entries, static e => Assert.True(e.Level < LogLevel.Warning));
	}

	private static async Task UsingPipelineAsync(
		IEnumerable<TelegramRouteContribution> contributions,
		Update update,
		Func<TelegramRouter, CallbackAnswerObligation, RecordingTelegramBotClient, IServiceProvider, Update, Task> body)
	{
		var bot = new RecordingTelegramBotClient();
		var router = new TelegramRouter(contributions, bot, NullLogger<TelegramRouter>.Instance);
		var obligation = new CallbackAnswerObligation(bot, NullLogger<CallbackAnswerObligation>.Instance);

		var services = new ServiceCollection();
		_ = services.AddSingleton<ITelegramBotClient>(bot);
		_ = services.AddScoped<UpdateContextHolder>();
		_ = services.AddScoped<Feedback>();
		await using var provider = services.BuildServiceProvider(validateScopes: true);
		using var scope = provider.CreateScope();
		scope.ServiceProvider.GetRequiredService<UpdateContextHolder>().Set(update);
		await body(router, obligation, bot, scope.ServiceProvider, update);
	}

	private static TelegramRouteContribution CallbackContribution(string key, RouteBinder binder) =>
		new(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			callbacks: new Dictionary<string, RouteBinder>(StringComparer.Ordinal)
			{
				[key] = binder,
			});

	private static Update CallbackUpdate(string? data) =>
		new()
		{
			Id = 9,
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
}
