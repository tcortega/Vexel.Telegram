using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Vexel.Telegram.Handlers;
using Vexel.Telegram.Handlers.Contexts;
using Vexel.Telegram.Handlers.Routing;
using Vexel.Telegram.Tests.Fakes;

namespace Vexel.Telegram.Tests.Flows;

public sealed class FlowTests
{
	[Fact]
	public async Task PromptAsync_arms_step_with_default_ttl()
	{
		var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
		var store = new MemoryFlowStore(time);
		var stepKey = Flow.GetStepKey(typeof(StepHandler));

		await using var provider = BuildProvider(store, time, stepKey, out _);
		using var scope = provider.CreateScope();
		BindMessage(scope.ServiceProvider);
		var flow = scope.ServiceProvider.GetRequiredService<Flow>();

		await flow.PromptAsync<StepHandler>();

		var entry = await store.GetAsync(7, 9);
		Assert.NotNull(entry);
		Assert.Equal(stepKey, entry.StepKey);
		Assert.Equal(time.GetUtcNow().AddMinutes(15), entry.ExpiresAt);
		Assert.True(flow.WasRearmed);
	}

	[Fact]
	public async Task Draft_round_trips_json_poco()
	{
		var store = new MemoryFlowStore();
		var stepKey = Flow.GetStepKey(typeof(StepHandler));

		await using var provider = BuildProvider(store, TimeProvider.System, stepKey, out _);
		using var scope = provider.CreateScope();
		BindMessage(scope.ServiceProvider);
		var flow = scope.ServiceProvider.GetRequiredService<Flow>();

		await flow.PromptAsync<StepHandler>();
		await flow.SetDraftAsync(new DraftPoCo { Name = "Ada", Age = 36 });

		var draft = await flow.GetDraftAsync<DraftPoCo>();
		Assert.NotNull(draft);
		Assert.Equal("Ada", draft.Name);
		Assert.Equal(36, draft.Age);
	}

	[Fact]
	public async Task CancelAsync_clears_store()
	{
		var store = new MemoryFlowStore();
		var stepKey = Flow.GetStepKey(typeof(StepHandler));

		await using var provider = BuildProvider(store, TimeProvider.System, stepKey, out _);
		using var scope = provider.CreateScope();
		BindMessage(scope.ServiceProvider);
		var flow = scope.ServiceProvider.GetRequiredService<Flow>();

		await flow.PromptAsync<StepHandler>();
		await flow.CancelAsync();

		Assert.Null(await store.GetAsync(7, 9));
		Assert.True(flow.WasCleared);
		Assert.False(flow.WasRearmed);
	}

	private sealed class StepHandler;

	private sealed class DraftPoCo
	{
		public string Name { get; set; } = "";

		public int Age { get; set; }
	}

	private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => start;
	}

	private static ServiceProvider BuildProvider(
		IFlowStore store,
		TimeProvider time,
		string stepKey,
		out TelegramRouter router)
	{
		var bot = new RecordingTelegramBotClient();
		var contribution = new TelegramRouteContribution(
			"TestAsm",
			commands: new Dictionary<string, RouteBinder>(StringComparer.OrdinalIgnoreCase),
			commandMetadata: [],
			flowSteps: new Dictionary<string, RouteBinder>(StringComparer.Ordinal)
			{
				[stepKey] = static (_, _, _) => ValueTask.FromResult(true),
			});
		var options = Options.Create(new FlowOptions());
		router = new TelegramRouter([contribution], bot, NullLogger<TelegramRouter>.Instance, options);

		var services = new ServiceCollection();
		_ = services.AddSingleton<ITelegramBotClient>(bot);
		_ = services.AddSingleton(store);
		_ = services.AddSingleton(time);
		_ = services.AddSingleton(options);
		_ = services.AddSingleton(router);
		_ = services.AddScoped<UpdateContextHolder>();
		_ = services.AddScoped<Feedback>();
		_ = services.AddScoped<Flow>();
		return services.BuildServiceProvider(validateScopes: true);
	}

	private static void BindMessage(IServiceProvider scope) =>
		scope.GetRequiredService<UpdateContextHolder>().Set(new Update
		{
			Id = 1,
			Message = new Message
			{
				Id = 1,
				Date = DateTime.UtcNow,
				Chat = new Chat { Id = 7 },
				From = new User { Id = 9, IsBot = false, FirstName = "u" },
				Text = "hi",
			},
		});
}
