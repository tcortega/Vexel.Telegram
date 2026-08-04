using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Vexel.Telegram.Handlers;
using Vexel.Telegram.Handlers.Contexts;
using Vexel.Telegram.Handlers.Routing;
using Vexel.Telegram.Tests.Fakes;

namespace Vexel.Telegram.Tests.Flows;

public sealed class FlowRouterTests
{
	// Step keys are request-type FullNames: a nested request record renders as Handler+Request.
	private const string StepKey = "Demo.CollectName+Command";

	[Fact]
	public async Task Arm_then_text_invokes_flow_binder()
	{
		string? seen = null;
		var store = new MemoryFlowStore();
		await using var provider = BuildProvider(store, out var router, FlowContribution(
			StepKey,
			(_, payload, _) =>
			{
				seen = payload;
				return ValueTask.FromResult(true);
			}));

		using (var armScope = provider.CreateScope())
		{
			BindMessage(armScope.ServiceProvider, TextUpdate("hi"));
			var flow = armScope.ServiceProvider.GetRequiredService<Flow>();
			// PromptAsync validates against the composed map; seed via store + HasFlowStep path.
			await store.SetAsync(
				chatId: 7,
				userId: 9,
				new FlowEntry(StepKey, DraftJson: null, DateTimeOffset.UtcNow.AddMinutes(15)));
			_ = flow; // scope constructs Flow with router that knows the step
		}

		using var scope = provider.CreateScope();
		var update = TextUpdate("Alice");
		BindMessage(scope.ServiceProvider, update);
		await router.RouteAsync(update, scope.ServiceProvider, CancellationToken.None);

		Assert.Equal("Alice", seen);
	}

	[Fact]
	public async Task Success_without_rearm_auto_completes()
	{
		var store = new MemoryFlowStore();
		await store.SetAsync(7, 9, new FlowEntry(StepKey, DraftJson: null, DateTimeOffset.UtcNow.AddMinutes(15)));

		await using var provider = BuildProvider(store, out var router, FlowContribution(
			StepKey,
			static (_, _, _) => ValueTask.FromResult(true)));

		using var scope = provider.CreateScope();
		var update = TextUpdate("done");
		BindMessage(scope.ServiceProvider, update);
		await router.RouteAsync(update, scope.ServiceProvider, CancellationToken.None);

		Assert.Null(await store.GetAsync(7, 9));
	}

	[Fact]
	public async Task Success_with_rearm_keeps_new_step()
	{
		var nextKey = Flow.GetStepKey(typeof(CollectAgeRequest));
		var store = new MemoryFlowStore();
		await store.SetAsync(7, 9, new FlowEntry(StepKey, DraftJson: null, DateTimeOffset.UtcNow.AddMinutes(15)));

		var contribution = new TelegramRouteContribution(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			flowSteps: new Dictionary<string, RouteBinder>(StringComparer.Ordinal)
			{
				[StepKey] = async (scope, _, ct) =>
				{
					var flow = scope.GetRequiredService<Flow>();
					await flow.PromptAsync<CollectAgeRequest>(cancellationToken: ct);
					return true;
				},
				[nextKey] = static (_, _, _) => ValueTask.FromResult(true),
			});

		await using var provider = BuildProvider(store, out var router, contribution);

		using var scope = provider.CreateScope();
		var update = TextUpdate("Alice");
		BindMessage(scope.ServiceProvider, update);
		await router.RouteAsync(update, scope.ServiceProvider, CancellationToken.None);

		var remaining = await store.GetAsync(7, 9);
		Assert.NotNull(remaining);
		Assert.Equal(nextKey, remaining.StepKey);
	}

	[Fact]
	public async Task Step_throw_keeps_armed_and_sends_generic_reply()
	{
		var store = new MemoryFlowStore();
		await store.SetAsync(7, 9, new FlowEntry(StepKey, DraftJson: null, DateTimeOffset.UtcNow.AddMinutes(15)));

		await using var provider = BuildProvider(
			store,
			out var router,
			FlowContribution(StepKey, static (_, _, _) => throw new InvalidOperationException("boom")),
			out var bot);

		using var scope = provider.CreateScope();
		var update = TextUpdate("bad");
		BindMessage(scope.ServiceProvider, update);
		await router.RouteAsync(update, scope.ServiceProvider, CancellationToken.None);

		Assert.NotNull(await store.GetAsync(7, 9));
		var sent = Assert.Single(bot.OfType<SendMessageRequest>());
		Assert.Equal("Something went wrong - try again, or /cancel.", sent.Text);
	}

	[Fact]
	public async Task Unknown_slash_command_mid_flow_does_not_eat_message()
	{
		var invoked = new StrongBox<bool>(value: false);
		var store = new MemoryFlowStore();
		await store.SetAsync(7, 9, new FlowEntry(StepKey, DraftJson: null, DateTimeOffset.UtcNow.AddMinutes(15)));

		await using var provider = BuildProvider(store, out var router, FlowContribution(
			StepKey,
			(_, _, _) =>
			{
				invoked.Value = true;
				return ValueTask.FromResult(true);
			}));

		using var scope = provider.CreateScope();
		var update = CommandUpdate("/foo");
		BindMessage(scope.ServiceProvider, update);
		await router.RouteAsync(update, scope.ServiceProvider, CancellationToken.None);

		Assert.False(invoked.Value);
		// State remains armed - message was not consumed as a step answer.
		Assert.NotNull(await store.GetAsync(7, 9));
	}

	[Fact]
	public async Task Command_for_another_bot_mid_flow_does_not_eat_message()
	{
		string? seen = null;
		var store = new MemoryFlowStore();
		await store.SetAsync(7, 9, new FlowEntry(StepKey, DraftJson: null, DateTimeOffset.UtcNow.AddMinutes(15)));

		await using var provider = BuildProvider(store, out var router, FlowContribution(
			StepKey,
			(_, payload, _) =>
			{
				seen = payload;
				return ValueTask.FromResult(true);
			}));

		using var scope = provider.CreateScope();
		var update = CommandUpdate("/stats@OtherBot");
		BindMessage(scope.ServiceProvider, update);
		await router.RouteAsync(update, scope.ServiceProvider, CancellationToken.None);

		Assert.Null(seen);
		// C2: an offset-0 BotCommand entity is never step input, even when it targets another bot.
		Assert.NotNull(await store.GetAsync(7, 9));
	}

	[Fact]
	public async Task Built_in_cancel_without_armed_flow_stays_silent()
	{
		var store = new MemoryFlowStore();

		await using var provider = BuildProvider(
			store,
			out var router,
			FlowContribution(StepKey, static (_, _, _) => ValueTask.FromResult(true)),
			out var bot);

		using var scope = provider.CreateScope();
		var update = CommandUpdate("/cancel");
		BindMessage(scope.ServiceProvider, update);
		await router.RouteAsync(update, scope.ServiceProvider, CancellationToken.None);

		Assert.Empty(bot.OfType<SendMessageRequest>());
	}

	[Fact]
	public async Task Built_in_cancel_stays_silent_without_registered_flow_steps()
	{
		var store = new MemoryFlowStore();
		await store.SetAsync(7, 9, new FlowEntry(StepKey, DraftJson: null, DateTimeOffset.UtcNow.AddMinutes(15)));

		var contribution = new TelegramRouteContribution(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			flowSteps: new Dictionary<string, RouteBinder>(StringComparer.Ordinal));

		await using var provider = BuildProvider(store, out var router, contribution, out var bot);

		using var scope = provider.CreateScope();
		var update = CommandUpdate("/cancel");
		BindMessage(scope.ServiceProvider, update);
		await router.RouteAsync(update, scope.ServiceProvider, CancellationToken.None);

		Assert.Empty(bot.OfType<SendMessageRequest>());
	}

	[Fact]
	public async Task Built_in_cancel_clears_state_and_confirms()
	{
		var store = new MemoryFlowStore();
		await store.SetAsync(7, 9, new FlowEntry(StepKey, DraftJson: null, DateTimeOffset.UtcNow.AddMinutes(15)));

		await using var provider = BuildProvider(
			store,
			out var router,
			FlowContribution(StepKey, static (_, _, _) => ValueTask.FromResult(true)),
			out var bot);

		using var scope = provider.CreateScope();
		var update = CommandUpdate("/cancel");
		BindMessage(scope.ServiceProvider, update);
		await router.RouteAsync(update, scope.ServiceProvider, CancellationToken.None);

		Assert.Null(await store.GetAsync(7, 9));
		var sent = Assert.Single(bot.OfType<SendMessageRequest>());
		Assert.Equal("Cancelled.", sent.Text);
	}

	[Fact]
	public async Task User_defined_cancel_command_wins_over_built_in()
	{
		var store = new MemoryFlowStore();
		await store.SetAsync(7, 9, new FlowEntry(StepKey, DraftJson: null, DateTimeOffset.UtcNow.AddMinutes(15)));

		var userCancelInvoked = false;
		var contribution = new TelegramRouteContribution(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase)
			{
				["cancel"] = (_, _, _) =>
				{
					userCancelInvoked = true;
					return ValueTask.FromResult(true);
				},
			},
			commandMetadata: [],
			flowSteps: new Dictionary<string, RouteBinder>(StringComparer.Ordinal)
			{
				[StepKey] = static (_, _, _) => ValueTask.FromResult(true),
			});

		await using var provider = BuildProvider(store, out var router, contribution, out var bot);

		using var scope = provider.CreateScope();
		var update = CommandUpdate("/cancel");
		BindMessage(scope.ServiceProvider, update);
		await router.RouteAsync(update, scope.ServiceProvider, CancellationToken.None);

		Assert.True(userCancelInvoked);
		// Built-in did not run - no automatic confirmation, state untouched by built-in.
		Assert.Empty(bot.OfType<SendMessageRequest>());
		Assert.NotNull(await store.GetAsync(7, 9));
	}

	[Fact]
	public async Task Caption_binds_as_flow_payload()
	{
		string? seen = null;
		var store = new MemoryFlowStore();
		await store.SetAsync(7, 9, new FlowEntry(StepKey, DraftJson: null, DateTimeOffset.UtcNow.AddMinutes(15)));

		await using var provider = BuildProvider(store, out var router, FlowContribution(
			StepKey,
			(_, payload, _) =>
			{
				seen = payload;
				return ValueTask.FromResult(true);
			}));

		using var scope = provider.CreateScope();
		var update = CaptionUpdate("photo note");
		BindMessage(scope.ServiceProvider, update);
		await router.RouteAsync(update, scope.ServiceProvider, CancellationToken.None);

		Assert.Equal("photo note", seen);
	}

	[Fact]
	public async Task Stale_step_key_clears_state_without_invoking_handler()
	{
		var invoked = false;
		var store = new MemoryFlowStore();
		await store.SetAsync(
			7,
			9,
			new FlowEntry("Demo.Missing+Command", DraftJson: null, DateTimeOffset.UtcNow.AddMinutes(15)));

		await using var provider = BuildProvider(store, out var router, FlowContribution(
			StepKey,
			(_, _, _) =>
			{
				invoked = true;
				return ValueTask.FromResult(true);
			}));

		using var scope = provider.CreateScope();
		var update = TextUpdate("hello");
		BindMessage(scope.ServiceProvider, update);
		await router.RouteAsync(update, scope.ServiceProvider, CancellationToken.None);

		Assert.False(invoked);
		Assert.Null(await store.GetAsync(7, 9));
	}

	[Fact]
	public async Task Success_with_cancel_inside_step_does_not_rearm()
	{
		var store = new MemoryFlowStore();
		await store.SetAsync(7, 9, new FlowEntry(StepKey, DraftJson: null, DateTimeOffset.UtcNow.AddMinutes(15)));

		await using var provider = BuildProvider(store, out var router, FlowContribution(
			StepKey,
			async (scope, _, ct) =>
			{
				await scope.GetRequiredService<Flow>().CancelAsync(ct);
				return true;
			}));

		using var scope = provider.CreateScope();
		var update = TextUpdate("bye");
		BindMessage(scope.ServiceProvider, update);
		await router.RouteAsync(update, scope.ServiceProvider, CancellationToken.None);

		// CancelAsync already cleared; auto-complete path must not resurrect state.
		Assert.Null(await store.GetAsync(7, 9));
	}

	[Fact]
	public async Task Step_throw_can_opt_out_of_generic_error_reply()
	{
		var store = new MemoryFlowStore();
		await store.SetAsync(7, 9, new FlowEntry(StepKey, DraftJson: null, DateTimeOffset.UtcNow.AddMinutes(15)));

		var options = Options.Create(new FlowOptions { SendStepErrorReply = false });
		await using var provider = BuildProvider(
			store,
			out var router,
			FlowContribution(StepKey, static (_, _, _) => throw new InvalidOperationException("boom")),
			out var bot,
			options);

		using var scope = provider.CreateScope();
		var update = TextUpdate("bad");
		BindMessage(scope.ServiceProvider, update);
		await router.RouteAsync(update, scope.ServiceProvider, CancellationToken.None);

		Assert.NotNull(await store.GetAsync(7, 9));
		Assert.Empty(bot.OfType<SendMessageRequest>());
	}

	[Fact]
	public async Task Flow_step_binding_failure_keeps_state_armed()
	{
		var store = new MemoryFlowStore();
		await store.SetAsync(7, 9, new FlowEntry(StepKey, DraftJson: null, DateTimeOffset.UtcNow.AddMinutes(15)));

		await using var provider = BuildProvider(store, out var router, FlowContribution(
			StepKey,
			static (_, _, _) => ValueTask.FromResult(false)));

		using var scope = provider.CreateScope();
		var update = TextUpdate("not-an-int");
		BindMessage(scope.ServiceProvider, update);
		await router.RouteAsync(update, scope.ServiceProvider, CancellationToken.None);

		Assert.NotNull(await store.GetAsync(7, 9));
	}

	[Fact]
	public async Task Message_without_From_skips_flow_step()
	{
		var invoked = false;
		var store = new MemoryFlowStore();
		await store.SetAsync(7, 9, new FlowEntry(StepKey, DraftJson: null, DateTimeOffset.UtcNow.AddMinutes(15)));

		await using var provider = BuildProvider(store, out var router, FlowContribution(
			StepKey,
			(_, _, _) =>
			{
				invoked = true;
				return ValueTask.FromResult(true);
			}));

		using var scope = provider.CreateScope();
		var update = new Update
		{
			Id = 99,
			Message = new Message
			{
				Id = 99,
				Date = DateTime.UtcNow,
				Chat = new Chat { Id = 7 },
				// No From - channel-style or anonymous admin edge.
				Text = "Alice",
			},
		};
		BindMessage(scope.ServiceProvider, update);
		await router.RouteAsync(update, scope.ServiceProvider, CancellationToken.None);

		Assert.False(invoked);
		Assert.NotNull(await store.GetAsync(7, 9));
	}

	[Fact]
	public async Task Known_command_wins_over_armed_flow_step()
	{
		var stepInvoked = false;
		var commandInvoked = false;
		var store = new MemoryFlowStore();
		await store.SetAsync(7, 9, new FlowEntry(StepKey, DraftJson: null, DateTimeOffset.UtcNow.AddMinutes(15)));

		var contribution = new TelegramRouteContribution(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase)
			{
				["ping"] = (_, _, _) =>
				{
					commandInvoked = true;
					return ValueTask.FromResult(true);
				},
			},
			commandMetadata: [],
			flowSteps: new Dictionary<string, RouteBinder>(StringComparer.Ordinal)
			{
				[StepKey] = (_, _, _) =>
				{
					stepInvoked = true;
					return ValueTask.FromResult(true);
				},
			});

		await using var provider = BuildProvider(store, out var router, contribution);

		using var scope = provider.CreateScope();
		var update = CommandUpdate("/ping");
		BindMessage(scope.ServiceProvider, update);
		await router.RouteAsync(update, scope.ServiceProvider, CancellationToken.None);

		Assert.True(commandInvoked);
		Assert.False(stepInvoked);
		// Command path does not auto-complete the armed flow.
		Assert.NotNull(await store.GetAsync(7, 9));
	}

	/// <summary>Stands in for the next step's request type, whose FullName is the step key.</summary>
	private sealed record CollectAgeRequest;

	private static TelegramRouteContribution FlowContribution(string stepKey, RouteBinder binder) =>
		new(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			flowSteps: new Dictionary<string, RouteBinder>(StringComparer.Ordinal)
			{
				[stepKey] = binder,
			});

	private static ServiceProvider BuildProvider(
		IFlowStore store,
		out TelegramRouter router,
		TelegramRouteContribution contribution) =>
		BuildProvider(store, out router, contribution, out _, Options.Create(new FlowOptions()));

	private static ServiceProvider BuildProvider(
		IFlowStore store,
		out TelegramRouter router,
		TelegramRouteContribution contribution,
		out RecordingTelegramBotClient bot) =>
		BuildProvider(store, out router, contribution, out bot, Options.Create(new FlowOptions()));

	private static ServiceProvider BuildProvider(
		IFlowStore store,
		out TelegramRouter router,
		TelegramRouteContribution contribution,
		out RecordingTelegramBotClient bot,
		IOptions<FlowOptions> options)
	{
		bot = new RecordingTelegramBotClient { Username = "TestBot" };
		router = new TelegramRouter([contribution], bot, NullLogger<TelegramRouter>.Instance, options);

		var services = new ServiceCollection();
		_ = services.AddSingleton<ITelegramBotClient>(bot);
		_ = services.AddSingleton(store);
		_ = services.AddSingleton(TimeProvider.System);
		_ = services.AddSingleton(options);
		_ = services.AddSingleton(router);
		_ = services.AddScoped<UpdateContextHolder>();
		_ = services.AddScoped<Feedback>();
		_ = services.AddScoped<Flow>();
		return services.BuildServiceProvider(validateScopes: true);
	}

	private static void BindMessage(IServiceProvider scope, Update update) =>
		scope.GetRequiredService<UpdateContextHolder>().Set(update);

	private static Update TextUpdate(string text) =>
		new()
		{
			Id = 1,
			Message = new Message
			{
				Id = 1,
				Date = DateTime.UtcNow,
				Chat = new Chat { Id = 7 },
				From = new User { Id = 9, IsBot = false, FirstName = "u" },
				Text = text,
			},
		};

	private static Update CaptionUpdate(string caption) =>
		new()
		{
			Id = 2,
			Message = new Message
			{
				Id = 2,
				Date = DateTime.UtcNow,
				Chat = new Chat { Id = 7 },
				From = new User { Id = 9, IsBot = false, FirstName = "u" },
				Caption = caption,
			},
		};

	private static Update CommandUpdate(string text)
	{
		var slash = text.IndexOf(' ', StringComparison.Ordinal);
		var entityLength = slash < 0 ? text.Length : slash;
		return new Update
		{
			Id = 3,
			Message = new Message
			{
				Id = 3,
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
}
