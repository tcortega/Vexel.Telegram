using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
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
			[new CommandRouteMetadata("ping", "Ping")]);

		var bot = new RecordingTelegramBotClient { Username = "TestBot" };
		var router = new TelegramRouter([contribution], bot, NullLogger<TelegramRouter>.Instance);
		var services = new ServiceCollection().BuildServiceProvider();

		await router.RouteAsync(CommandUpdate("/ping hello"), services, CancellationToken.None);

		Assert.Equal("hello", seenPayload);
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
