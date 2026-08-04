using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
