using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Vexel.Telegram.Client;
using Vexel.Telegram.Handlers.Routing;
using Vexel.Telegram.Tests.Fakes;

namespace Vexel.Telegram.Tests.Routing;

public sealed class TelegramRouterTests
{
	[Fact]
	public async Task Routes_command_to_binder()
	{
		string? seenPayload = null;
		var contribution = new TelegramRouteContribution(
			"TestAsm",
			new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase)
			{
				["ping"] = (scope, payload, ct) =>
				{
					_ = scope;
					_ = ct;
					seenPayload = payload;
					return ValueTask.FromResult(true);
				},
			},
			[new BotCommandDescriptor("ping", "Ping")]);

		var bot = new RecordingTelegramBotClient { Username = "TestBot" };
		var router = new TelegramRouter([contribution], bot, NullLogger<TelegramRouter>.Instance);
		var services = new ServiceCollection().BuildServiceProvider();

		await router.RouteAsync(CommandUpdate("/ping hello"), services, CancellationToken.None);

		Assert.Equal("hello", seenPayload);
	}

	[Theory]
	[InlineData("/PING hello", "hello")]
	[InlineData("/Ping", "")]
	[InlineData("/pInG  x y", "x y")]
	public async Task Command_keys_match_case_insensitively(string text, string expectedPayload)
	{
		string? seenPayload = null;
		var contribution = new TelegramRouteContribution(
			"TestAsm",
			new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase)
			{
				["ping"] = (_, payload, _) =>
				{
					seenPayload = payload;
					return ValueTask.FromResult(true);
				},
			},
			[]);

		var router = new TelegramRouter(
			[contribution],
			new RecordingTelegramBotClient { Username = "TestBot" },
			NullLogger<TelegramRouter>.Instance);

		await router.RouteAsync(
			CommandUpdate(text),
			new ServiceCollection().BuildServiceProvider(),
			CancellationToken.None);

		Assert.Equal(expectedPayload, seenPayload);
	}

	[Fact]
	public async Task Binding_failure_logs_warning_and_skips_handler_body()
	{
		// Models generated binders: TryParse fails → return false before HandleAsync side effects.
		var handlerBodyRan = false;
		var logger = new RecordingLogger<TelegramRouter>();
		var contribution = new TelegramRouteContribution(
			"TestAsm",
			new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase)
			{
				["add"] = (_, payload, _) =>
				{
					if (!CommandArgumentBinder.TryParseInt(payload, out _))
					{
						return ValueTask.FromResult(false);
					}

					handlerBodyRan = true;
					return ValueTask.FromResult(true);
				},
			},
			[]);

		var router = new TelegramRouter(
			[contribution],
			new RecordingTelegramBotClient { Username = "TestBot" },
			logger);

		await router.RouteAsync(
			CommandUpdate("/add abc"),
			new ServiceCollection().BuildServiceProvider(),
			CancellationToken.None);

		Assert.False(handlerBodyRan);
		Assert.Contains(
			logger.Entries,
			static e => e.Level == LogLevel.Warning
				&& e.Message.Contains("Failed to bind arguments for /add", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Command_handler_exception_is_isolated_and_does_not_throw()
	{
		var logger = new RecordingLogger<TelegramRouter>();
		var contribution = new TelegramRouteContribution(
			"TestAsm",
			new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase)
			{
				["boom"] = static (_, _, _) => throw new InvalidOperationException("handler boom"),
			},
			[]);

		var router = new TelegramRouter(
			[contribution],
			new RecordingTelegramBotClient { Username = "TestBot" },
			logger);

		var ex = await Record.ExceptionAsync(() =>
			router.RouteAsync(
				CommandUpdate("/boom"),
				new ServiceCollection().BuildServiceProvider(),
				CancellationToken.None));

		Assert.Null(ex);
		Assert.Contains(
			logger.Entries,
			static e => e.Level == LogLevel.Error
				&& e.Message.Contains("Command handler for /boom failed", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Skips_unknown_command()
	{
		var invoked = false;
		var contribution = new TelegramRouteContribution(
			"TestAsm",
			new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase)
			{
				["ping"] = (_, _, _) =>
				{
					invoked = true;
					return ValueTask.FromResult(true);
				},
			},
			[]);

		var router = new TelegramRouter(
			[contribution],
			new RecordingTelegramBotClient { Username = "TestBot" },
			NullLogger<TelegramRouter>.Instance);

		await router.RouteAsync(
			CommandUpdate("/other"),
			new ServiceCollection().BuildServiceProvider(),
			CancellationToken.None);

		Assert.False(invoked);
	}

	[Fact]
	public void Duplicate_cross_assembly_keys_fail_fast()
	{
		static TelegramRouteContribution Make(string asm) => new(
			asm,
			new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase)
			{
				["ping"] = static (_, _, _) => ValueTask.FromResult(true),
			},
			[]);

		var ex = Assert.Throws<InvalidOperationException>(() =>
			_ = new TelegramRouter(
				[Make("A"), Make("B")],
				new RecordingTelegramBotClient(),
				NullLogger<TelegramRouter>.Instance));

		Assert.Contains("Duplicate command route 'ping'", ex.Message, StringComparison.Ordinal);
		Assert.Contains("'A'", ex.Message, StringComparison.Ordinal);
		Assert.Contains("'B'", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Same_assembly_contributed_twice_points_at_the_double_registration()
	{
		static TelegramRouteContribution Make() => new(
			"MyApp",
			new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase)
			{
				["ping"] = static (_, _, _) => ValueTask.FromResult(true),
			},
			[]);

		var ex = Assert.Throws<InvalidOperationException>(() =>
			_ = new TelegramRouter(
				[Make(), Make()],
				new RecordingTelegramBotClient(),
				NullLogger<TelegramRouter>.Instance));

		Assert.Contains("more than once", ex.Message, StringComparison.Ordinal);
		Assert.Contains("AddMyAppTelegram()", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Duplicate_cross_assembly_flow_steps_fail_fast()
	{
		static TelegramRouteContribution Make(string asm) => new(
			asm,
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			flowSteps: new Dictionary<string, RouteBinder>(StringComparer.Ordinal)
			{
				["Demo.Step+Command"] = static (_, _, _) => ValueTask.FromResult(true),
			});

		var ex = Assert.Throws<InvalidOperationException>(() =>
			_ = new TelegramRouter(
				[Make("A"), Make("B")],
				new RecordingTelegramBotClient(),
				NullLogger<TelegramRouter>.Instance));

		Assert.Contains("Duplicate flow step 'Demo.Step+Command'", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Plain_command_does_not_call_GetMe()
	{
		var bot = new RecordingTelegramBotClient { Username = "TestBot" };
		var router = new TelegramRouter(
			[PingContribution(out _)],
			bot,
			NullLogger<TelegramRouter>.Instance);

		await router.RouteAsync(
			CommandUpdate("/ping hello"),
			new ServiceCollection().BuildServiceProvider(),
			CancellationToken.None);

		Assert.Empty(bot.OfType<GetMeRequest>());
	}

	[Fact]
	public async Task Suffixed_command_for_another_bot_is_skipped()
	{
		var contribution = PingContribution(out var invoked);
		var bot = new RecordingTelegramBotClient { Username = "TestBot" };
		var router = new TelegramRouter([contribution], bot, NullLogger<TelegramRouter>.Instance);

		await router.RouteAsync(
			CommandUpdate("/ping@OtherBot"),
			new ServiceCollection().BuildServiceProvider(),
			CancellationToken.None);

		Assert.False(invoked.Value);
		Assert.Single(bot.OfType<GetMeRequest>());
	}

	[Fact]
	public async Task Suffixed_command_for_this_bot_is_routed()
	{
		var contribution = PingContribution(out var invoked);
		var bot = new RecordingTelegramBotClient { Username = "TestBot" };
		var router = new TelegramRouter([contribution], bot, NullLogger<TelegramRouter>.Instance);

		await router.RouteAsync(
			CommandUpdate("/ping@TestBot args"),
			new ServiceCollection().BuildServiceProvider(),
			CancellationToken.None);

		Assert.True(invoked.Value);
	}

	[Fact]
	public async Task BotUsernameOverride_skips_GetMe_for_suffixed_commands()
	{
		var contribution = PingContribution(out var invoked);
		var bot = new RecordingTelegramBotClient { Username = "TestBot" };
		var router = new TelegramRouter([contribution], bot, NullLogger<TelegramRouter>.Instance)
		{
			BotUsernameOverride = "TestBot",
		};

		await router.RouteAsync(
			CommandUpdate("/ping@TestBot"),
			new ServiceCollection().BuildServiceProvider(),
			CancellationToken.None);

		Assert.True(invoked.Value);
		Assert.Empty(bot.OfType<GetMeRequest>());
	}

	[Fact]
	public async Task GetMe_failure_is_retried_and_does_not_latch()
	{
		var contribution = PingContribution(out var invoked);
		var bot = new RecordingTelegramBotClient
		{
			Username = "TestBot",
			FailRequest = static request =>
				request is GetMeRequest ? new InvalidOperationException("boom") : null,
		};

		var router = new TelegramRouter([contribution], bot, NullLogger<TelegramRouter>.Instance)
		{
			BotUsernameRetryBackoff = TimeSpan.Zero,
		};

		var services = new ServiceCollection().BuildServiceProvider();

		// Unknown username degrades to matching the bare command, so the failed lookup is observable
		// only through the retry: a permanent latch would keep answering /ping@OtherBot forever.
		await router.RouteAsync(CommandUpdate("/ping@OtherBot"), services, CancellationToken.None);
		Assert.True(invoked.Value);

		invoked.Value = false;
		bot.FailRequest = null;

		await router.RouteAsync(CommandUpdate("/ping@OtherBot"), services, CancellationToken.None);

		Assert.False(invoked.Value);
		Assert.Equal(2, bot.OfType<GetMeRequest>().Count);
	}

	[Fact]
	public async Task Raw_only_update_kinds_do_not_invoke_message_routes_or_OnMessage()
	{
		var invoked = false;
		var contribution = new TelegramRouteContribution(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase)
			{
				["ping"] = (_, _, _) =>
				{
					invoked = true;
					return ValueTask.FromResult(true);
				},
			},
			commandMetadata: [],
			onMessages:
			[
				new OnHandlerEntry(
					"global::Demo.Observer",
					(_, _, _) =>
					{
						invoked = true;
						return ValueTask.FromResult(true);
					}),
			]);

		var router = new TelegramRouter(
			[contribution],
			new RecordingTelegramBotClient { Username = "TestBot" },
			NullLogger<TelegramRouter>.Instance);

		var services = new ServiceCollection().BuildServiceProvider();

		// EditedMessage / Poll / ChannelPost are raw-only in 2.0 - router returns without work.
		await router.RouteAsync(
			new Update
			{
				Id = 1,
				EditedMessage = new Message
				{
					Id = 1,
					Date = DateTime.UtcNow,
					Chat = new Chat { Id = 7 },
					Text = "/ping",
					Entities =
					[
						new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 0, Length = 5 },
					],
				},
			},
			services,
			CancellationToken.None);

		await router.RouteAsync(
			new Update
			{
				Id = 2,
				Poll = new Poll
				{
					Id = "p",
					Question = "q",
					Options = [],
					TotalVoterCount = 0,
					IsClosed = false,
					IsAnonymous = true,
					Type = PollType.Regular,
					AllowsMultipleAnswers = false,
				},
			},
			services,
			CancellationToken.None);

		await router.RouteAsync(
			new Update
			{
				Id = 3,
				ChannelPost = new Message
				{
					Id = 3,
					Date = DateTime.UtcNow,
					Chat = new Chat { Id = 8, Type = ChatType.Channel },
					Text = "/ping",
				},
			},
			services,
			CancellationToken.None);

		Assert.False(invoked);
	}

	[Fact]
	public void CommandMetadata_is_sorted_by_name()
	{
		var contribution = new TelegramRouteContribution(
			"TestAsm",
			new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase)
			{
				["zeta"] = static (_, _, _) => ValueTask.FromResult(true),
				["alpha"] = static (_, _, _) => ValueTask.FromResult(true),
			},
			[
				new BotCommandDescriptor("zeta", "Z"),
				new BotCommandDescriptor("alpha", "A"),
			]);

		var router = new TelegramRouter(
			[contribution],
			new RecordingTelegramBotClient(),
			NullLogger<TelegramRouter>.Instance);

		Assert.Equal(["alpha", "zeta"], router.CommandMetadata.Select(static m => m.Name));
	}

	private static TelegramRouteContribution PingContribution(out StrongBox<bool> invoked)
	{
		var flag = new StrongBox<bool>(value: false);
		invoked = flag;
		return new TelegramRouteContribution(
			"TestAsm",
			new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase)
			{
				["ping"] = (_, _, _) =>
				{
					flag.Value = true;
					return ValueTask.FromResult(true);
				},
			},
			[]);
	}

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
				Text = text,
				Entities =
				[
					new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 0, Length = entityLength },
				],
			},
		};
	}
}
