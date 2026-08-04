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
	public async Task VEX0001_fires_when_Callback_missing_Handler()
	{
		const string source = """
			using Vexel.Telegram.Handlers.Attributes;

			namespace Demo;

			[Callback("confirm")]
			public static class Confirm
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
	public async Task VEX0002_fires_when_callback_key_exceeds_64_utf8_bytes()
	{
		// 65 ASCII bytes.
		var key = new string('a', 65);
		var source = $$"""
			using Immediate.Handlers.Shared;
			using Vexel.Telegram.Handlers.Attributes;

			namespace Demo;

			[Handler]
			[Callback("{{key}}")]
			public static class TooLong
			{
				public sealed record Command;
			}
			""";

		var diagnostics = await GeneratorTestHelper.RunAnalyzersAsync(
			source,
			new CallbackDataTooLongAnalyzer());

		Assert.Contains(diagnostics, static d => d.Id == "VEX0002");
	}

	[Fact]
	public async Task VEX0002_fires_when_callback_key_is_blank()
	{
		const string source = """
			using Immediate.Handlers.Shared;
			using Vexel.Telegram.Handlers.Attributes;

			namespace Demo;

			[Handler]
			[Callback("  ")]
			public static class Blank
			{
				public sealed record Command;
			}
			""";

		var diagnostics = await GeneratorTestHelper.RunAnalyzersAsync(
			source,
			new CallbackDataTooLongAnalyzer());

		Assert.Contains(diagnostics, static d => d.Id == "VEX0002");
	}

	[Fact]
	public async Task VEX0002_fires_when_callback_key_contains_pipe()
	{
		const string source = """
			using Immediate.Handlers.Shared;
			using Vexel.Telegram.Handlers.Attributes;

			namespace Demo;

			[Handler]
			[Callback("bad|key")]
			public static class BadKey
			{
				public sealed record Command;
			}
			""";

		var diagnostics = await GeneratorTestHelper.RunAnalyzersAsync(
			source,
			new CallbackDataTooLongAnalyzer());

		Assert.Contains(diagnostics, static d => d.Id == "VEX0002");
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

	[Fact]
	public async Task VEX0005_fires_on_duplicate_callback_keys()
	{
		const string source = """
			using System.Threading;
			using System.Threading.Tasks;
			using Immediate.Handlers.Shared;
			using Vexel.Telegram.Handlers.Attributes;

			namespace Demo;

			[Handler]
			[Callback("go")]
			public static class GoA
			{
				public sealed record Command;
				private static ValueTask HandleAsync(Command _, CancellationToken token) => default;
			}

			[Handler]
			[Callback("go")]
			public static class GoB
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

	[Fact]
	public async Task VEX0004_fires_on_unbindable_callback_request()
	{
		const string source = """
			using System.Threading;
			using System.Threading.Tasks;
			using Immediate.Handlers.Shared;
			using Vexel.Telegram.Handlers.Attributes;

			namespace Demo;

			[Handler]
			[Callback("go")]
			public static class Go
			{
				public sealed record Command(int Count);

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
	public async Task VEX0001_fires_when_InlineQuery_missing_Handler()
	{
		const string source = """
			using Vexel.Telegram.Handlers.Attributes;

			namespace Demo;

			[InlineQuery("search")]
			public static class Search
			{
				public sealed record Query(string Text);
			}
			""";

		var diagnostics = await GeneratorTestHelper.RunAnalyzersAsync(
			source,
			new MissingHandlerAttributeAnalyzer());

		Assert.Contains(diagnostics, static d => d.Id == "VEX0001");
	}

	[Fact]
	public async Task VEX0005_fires_on_duplicate_inline_query_defaults()
	{
		const string source = """
			using System.Threading;
			using System.Threading.Tasks;
			using Immediate.Handlers.Shared;
			using Vexel.Telegram.Handlers.Attributes;

			namespace Demo;

			[Handler]
			[InlineQuery]
			public static class DefaultA
			{
				public sealed record Query;
				private static ValueTask HandleAsync(Query _, CancellationToken token) => default;
			}

			[Handler]
			[InlineQuery("")]
			public static class DefaultB
			{
				public sealed record Query;
				private static ValueTask HandleAsync(Query _, CancellationToken token) => default;
			}
			""";

		var diagnostics = await GeneratorTestHelper.RunAnalyzersAsync(
			source,
			new DuplicateRouteKeyAnalyzer());

		Assert.Contains(diagnostics, static d => d.Id == "VEX0005");
	}

	[Fact]
	public async Task VEX0005_fires_on_duplicate_chosen_inline_result_keys()
	{
		const string source = """
			using System.Threading;
			using System.Threading.Tasks;
			using Immediate.Handlers.Shared;
			using Vexel.Telegram.Handlers.Attributes;

			namespace Demo;

			[Handler]
			[ChosenInlineResult("item")]
			public static class ItemA
			{
				public sealed record Command;
				private static ValueTask HandleAsync(Command _, CancellationToken token) => default;
			}

			[Handler]
			[ChosenInlineResult("item")]
			public static class ItemB
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

	[Fact]
	public async Task VEX0004_fires_on_unbindable_inline_query_request()
	{
		const string source = """
			using System.Threading;
			using System.Threading.Tasks;
			using Immediate.Handlers.Shared;
			using Vexel.Telegram.Handlers.Attributes;

			namespace Demo;

			[Handler]
			[InlineQuery("search")]
			public static class Search
			{
				public sealed record Query(int Count);

				private static ValueTask HandleAsync(Query _, CancellationToken token)
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
}
