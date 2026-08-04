using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot;
using Vexel.Telegram.Handlers.Routing;
using Vexel.Telegram.Tests.Fakes;

namespace Vexel.Telegram.Tests.Generators;

/// <summary>
/// Proves the flow step key the generator emits is the same string
/// <c>Flow.PromptAsync&lt;TRequest&gt;</c> computes at runtime, from source written the way a bot
/// author writes it: Immediate handler types are <see langword="static"/>, so the arming call must
/// name the nested request record, not the handler class.
/// </summary>
public sealed class FlowStepRegistrationTests
{
	private const string BotSource = """
		using System.Threading;
		using System.Threading.Tasks;
		using Immediate.Handlers.Shared;
		using Vexel.Telegram.Handlers;
		using Vexel.Telegram.Handlers.Attributes;

		namespace DemoFlowBot;

		[Handler]
		[Command("signup")]
		public static partial class StartSignup
		{
			public sealed record Command;

			private static async ValueTask HandleAsync(Command command, Flow flow, CancellationToken token)
			{
				// The idiomatic arming call: the request type, because CollectName itself is static.
				await flow.PromptAsync<CollectName.Command>(cancellationToken: token);
			}
		}

		[Handler]
		public static partial class CollectName
		{
			public sealed record Command(string Name);

			private static ValueTask HandleAsync(Command command, CancellationToken token)
			{
				_ = command;
				_ = token;
				return default;
			}
		}
		""";

	[Fact]
	public void Generated_step_key_matches_the_runtime_request_FullName()
	{
		var botAssembly = GeneratorTestHelper.EmitBotAssembly(BotSource, "DemoFlowBot", out var generatedRoutes);

		var requestType = botAssembly.GetType("DemoFlowBot.CollectName+Command", throwOnError: true)!;
		Assert.Contains("typeof(global::DemoFlowBot.CollectName.Command).FullName!", generatedRoutes, StringComparison.Ordinal);

		var services = new ServiceCollection();
		var registration = botAssembly
			.GetType("DemoFlowBot.TelegramServiceCollectionExtensions", throwOnError: true)!
			.GetMethod("AddDemoFlowBotTelegram")!;
		_ = registration.Invoke(null, [services]);

		var contribution = services
			.Select(static d => d.ImplementationInstance)
			.OfType<TelegramRouteContribution>()
			.Single();

		var router = new TelegramRouter(
			[contribution],
			new RecordingTelegramBotClient(),
			NullLogger<TelegramRouter>.Instance);

		Assert.True(router.HasFlowStep(requestType.FullName!));
	}
}
