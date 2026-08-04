namespace Vexel.Telegram.Tests.Generators;

/// <summary>
/// Risk-1 gate: Vexel-generated binders must compile against Immediate-generated X.Handler
/// in the same compilation (Immediate.Apis pattern).
/// </summary>
public sealed class CompositionSpikeTests
{
	[Fact]
	public void Generated_binder_resolves_Immediate_Handler_and_compiles()
	{
		const string source = """
			using System.Threading;
			using System.Threading.Tasks;
			using Immediate.Handlers.Shared;
			using Vexel.Telegram.Handlers.Attributes;

			namespace Demo;

			[Handler]
			[Command("ping")]
			public static partial class Ping
			{
				public sealed record Command;

				private static ValueTask HandleAsync(
					Command command,
					CancellationToken token)
				{
					_ = command;
					_ = token;
					return default;
				}
			}
			""";

		var result = GeneratorTestHelper.RunGenerators(source);
		var vexel = GeneratorTestHelper.GetVexelGeneratedSource(result);

		Assert.Contains("GetRequiredService<global::Demo.Ping.Handler>", vexel, StringComparison.Ordinal);
		Assert.Contains("AddGeneratorTestsTelegram", vexel, StringComparison.Ordinal);
		Assert.Contains(".HandleAsync(new global::Demo.Ping.Command()", vexel, StringComparison.Ordinal);

		// Immediate must have emitted the nested Handler type used above.
		Assert.Contains(
			result.GeneratedTrees,
			static t => t.GetText().ToString().Contains("class Handler", StringComparison.Ordinal)
				&& t.GetText().ToString().Contains("Ping", StringComparison.Ordinal));
	}
}
