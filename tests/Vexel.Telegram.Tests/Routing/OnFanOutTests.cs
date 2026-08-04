using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Vexel.Telegram.Handlers.Routing;
using Vexel.Telegram.Tests.Fakes;

namespace Vexel.Telegram.Tests.Routing;

/// <summary>
/// T8: proves routed-then-On* order and deterministic FQ-name ordering of On* observers.
/// </summary>
public sealed class OnFanOutTests
{
	[Fact]
	public async Task Routed_handler_runs_before_On_handlers()
	{
		var order = new List<string>();

		var contribution = new TelegramRouteContribution(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase)
			{
				["ping"] = (_, _, _) =>
				{
					order.Add("routed");
					return ValueTask.FromResult(true);
				},
			},
			commandMetadata: [new CommandRouteMetadata("ping", "Ping")],
			onMessages:
			[
				new OnHandlerEntry(
					"global::Demo.ZedObserver",
					(_, _, _) =>
					{
						order.Add("on:Zed");
						return ValueTask.FromResult(true);
					}),
			]);

		var router = CreateRouter(contribution);
		await router.RouteAsync(MessageUpdate("/ping", isCommand: true), EmptyScope(), CancellationToken.None);

		Assert.Equal(["routed", "on:Zed"], order);
	}

	[Fact]
	public async Task On_handlers_run_even_when_no_route_matches()
	{
		var onInvoked = false;

		var contribution = new TelegramRouteContribution(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
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

		var router = CreateRouter(contribution);
		await router.RouteAsync(MessageUpdate("plain text"), EmptyScope(), CancellationToken.None);

		Assert.True(onInvoked);
	}

	[Fact]
	public async Task On_handlers_run_in_fully_qualified_metadata_name_order()
	{
		var order = new List<string>();

		// Intentionally registered out of FQ-name order; router must re-sort.
		var contribution = new TelegramRouteContribution(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			onMessages:
			[
				new OnHandlerEntry("global::Demo.Zed", Record("Zed", order)),
				new OnHandlerEntry("global::Demo.Alpha", Record("Alpha", order)),
				new OnHandlerEntry("global::Demo.Mike", Record("Mike", order)),
			]);

		var router = CreateRouter(contribution);
		await router.RouteAsync(MessageUpdate("hi"), EmptyScope(), CancellationToken.None);

		Assert.Equal(["Alpha", "Mike", "Zed"], order);
	}

	[Fact]
	public async Task Cross_assembly_On_handlers_are_sorted_by_FQ_name_not_assembly_order()
	{
		var order = new List<string>();

		// B contributes Alpha; A contributes Zed. Registration order A then B; FQ order is Alpha, Zed.
		var a = new TelegramRouteContribution(
			"AsmA",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			onMessages: [new OnHandlerEntry("global::Demo.Zed", Record("Zed", order))]);

		var b = new TelegramRouteContribution(
			"AsmB",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			onMessages: [new OnHandlerEntry("global::Demo.Alpha", Record("Alpha", order))]);

		var router = new TelegramRouter(
			[a, b],
			new RecordingTelegramBotClient { Username = "TestBot" },
			NullLogger<TelegramRouter>.Instance);

		await router.RouteAsync(MessageUpdate("hi"), EmptyScope(), CancellationToken.None);

		Assert.Equal(["Alpha", "Zed"], order);
	}

	[Fact]
	public async Task On_handler_fault_does_not_skip_remaining_On_handlers()
	{
		var order = new List<string>();

		var contribution = new TelegramRouteContribution(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			onMessages:
			[
				new OnHandlerEntry(
					"global::Demo.Alpha",
					(_, _, _) => throw new InvalidOperationException("boom")),
				new OnHandlerEntry("global::Demo.Beta", Record("Beta", order)),
			]);

		var router = CreateRouter(contribution);
		await router.RouteAsync(MessageUpdate("hi"), EmptyScope(), CancellationToken.None);

		Assert.Equal(["Beta"], order);
	}

	[Fact]
	public async Task Callback_On_handlers_run_after_routed_callback()
	{
		var order = new List<string>();

		var contribution = new TelegramRouteContribution(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			callbacks: new Dictionary<string, RouteBinder>(StringComparer.Ordinal)
			{
				["ok"] = (_, _, _) =>
				{
					order.Add("routed");
					return ValueTask.FromResult(true);
				},
			},
			onCallbackQueries:
			[
				new OnHandlerEntry("global::Demo.Observer", Record("on", order)),
			]);

		var router = CreateRouter(contribution);
		var update = new Update
		{
			Id = 1,
			CallbackQuery = new CallbackQuery
			{
				Id = "cq1",
				From = new User { Id = 7, IsBot = false, FirstName = "U" },
				Data = "ok",
				ChatInstance = "x",
			},
		};

		await router.RouteAsync(update, EmptyScope(), CancellationToken.None);

		Assert.Equal(["routed", "on"], order);
	}

	private static RouteBinder Record(string name, List<string> order) =>
		(_, _, _) =>
		{
			order.Add(name);
			return ValueTask.FromResult(true);
		};

	private static TelegramRouter CreateRouter(TelegramRouteContribution contribution) =>
		new(
			[contribution],
			new RecordingTelegramBotClient { Username = "TestBot" },
			NullLogger<TelegramRouter>.Instance);

	private static ServiceProvider EmptyScope() =>
		new ServiceCollection().BuildServiceProvider();

	private static Update MessageUpdate(string text, bool isCommand = false)
	{
		MessageEntity[]? entities = null;
		if (isCommand)
		{
			var space = text.IndexOf(' ', StringComparison.Ordinal);
			var length = space < 0 ? text.Length : space;
			entities =
			[
				new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 0, Length = length },
			];
		}

		return new Update
		{
			Id = 42,
			Message = new Message
			{
				Id = 1,
				Date = DateTime.UtcNow,
				Chat = new Chat { Id = 100, Type = ChatType.Private },
				From = new User { Id = 7, IsBot = false, FirstName = "U" },
				Text = text,
				Entities = entities,
			},
		};
	}
}
