using Vexel.Telegram.Generators.Analyzers;

namespace Vexel.Telegram.Tests.Generators;

public sealed class AnalyzerTests
{
	[Fact]
	public async Task VEX0001_fires_when_Command_missing_Handler()
	{
		const string source = """
			using Vexel.Telegram.Handlers.Attributes;

			namespace Demo;

			[Command("ping")]
			public static class Ping
			{
				public sealed record Command;
			}
			""";

		var diagnostics = await GeneratorTestHelper.RunAnalyzersAsync(
			source,
			new MissingHandlerAttributeAnalyzer());

		Assert.Contains(diagnostics, static d => d.Id == "VEX0001");
	}

	[Fact]
	public async Task VEX0003_fires_on_invalid_command_name()
	{
		const string source = """
			using Immediate.Handlers.Shared;
			using Vexel.Telegram.Handlers.Attributes;

			namespace Demo;

			[Handler]
			[Command("Bad Name")]
			public static class Ping
			{
				public sealed record Command;
			}
			""";

		var diagnostics = await GeneratorTestHelper.RunAnalyzersAsync(
			source,
			new InvalidCommandNameAnalyzer());

		Assert.Contains(diagnostics, static d => d.Id == "VEX0003");
	}

	[Fact]
	public async Task VEX0004_fires_on_complex_request_type()
	{
		const string source = """
			using System.Threading;
			using System.Threading.Tasks;
			using Immediate.Handlers.Shared;
			using Vexel.Telegram.Handlers.Attributes;

			namespace Demo;

			[Handler]
			[Command("ping")]
			public static class Ping
			{
				public sealed record Command(string[] Items);

				private static ValueTask HandleAsync(Command _, CancellationToken token)
				{
					_ = token;
					return default;
				}
			}
			""";

		var diagnostics = await GeneratorTestHelper.RunAnalyzersAsync(
			source,
			new UnbindableRequestAnalyzer());

		Assert.Contains(diagnostics, static d => d.Id == "VEX0004");
	}

	[Fact]
	public async Task VEX0005_fires_on_duplicate_command_keys()
	{
		const string source = """
			using System.Threading;
			using System.Threading.Tasks;
			using Immediate.Handlers.Shared;
			using Vexel.Telegram.Handlers.Attributes;

			namespace Demo;

			[Handler]
			[Command("ping")]
			public static class PingA
			{
				public sealed record Command;
				private static ValueTask HandleAsync(Command _, CancellationToken token) => default;
			}

			[Handler]
			[Command("ping")]
			public static class PingB
			{
				public sealed record Command;
				private static ValueTask HandleAsync(Command _, CancellationToken token) => default;
			}
			""";

		var diagnostics = await GeneratorTestHelper.RunAnalyzersAsync(
			source,
			new DuplicateRouteKeyAnalyzer());

		Assert.Contains(diagnostics, static d => d.Id == "VEX0005");
	}
}
