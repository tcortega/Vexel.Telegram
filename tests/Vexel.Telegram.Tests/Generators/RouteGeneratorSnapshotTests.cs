namespace Vexel.Telegram.Tests.Generators;

public sealed class RouteGeneratorSnapshotTests
{
	[Fact]
	public async Task Empty_record_command_binder()
	{
		const string source = """
			using System.Threading;
			using System.Threading.Tasks;
			using Immediate.Handlers.Shared;
			using Vexel.Telegram.Handlers.Attributes;

			namespace Demo;

			[Handler]
			[Command("ping", Description = "Ping the bot")]
			public static partial class Ping
			{
				public sealed record Command;

				private static ValueTask HandleAsync(Command command, CancellationToken token)
				{
					_ = command;
					_ = token;
					return default;
				}
			}
			""";

		var generated = GeneratorTestHelper.GetVexelGeneratedSource(GeneratorTestHelper.RunGenerators(source));
		await Verify(generated);
	}

	[Fact]
	public async Task Single_string_and_multi_param_binders()
	{
		const string source = """
			using System.Threading;
			using System.Threading.Tasks;
			using Immediate.Handlers.Shared;
			using Vexel.Telegram.Handlers.Attributes;

			namespace Demo;

			[Handler]
			[Command("echo")]
			public static partial class Echo
			{
				public sealed record Command(string Text);

				private static ValueTask HandleAsync(Command command, CancellationToken token)
				{
					_ = command;
					_ = token;
					return default;
				}
			}

			[Handler]
			[Command("add")]
			public static partial class Add
			{
				public sealed record Command(int Left, int Right);

				private static ValueTask HandleAsync(Command command, CancellationToken token)
				{
					_ = command;
					_ = token;
					return default;
				}
			}

			[Handler]
			[Command("say")]
			public static partial class Say
			{
				public sealed record Command(int Times, string Text);

				private static ValueTask HandleAsync(Command command, CancellationToken token)
				{
					_ = command;
					_ = token;
					return default;
				}
			}
			""";

		var generated = GeneratorTestHelper.GetVexelGeneratedSource(GeneratorTestHelper.RunGenerators(source));
		await Verify(generated);
	}

	[Fact]
	public async Task Empty_and_string_callback_binders()
	{
		const string source = """
			using System.Threading;
			using System.Threading.Tasks;
			using Immediate.Handlers.Shared;
			using Vexel.Telegram.Handlers.Attributes;

			namespace Demo;

			[Handler]
			[Callback("confirm")]
			public static partial class Confirm
			{
				public sealed record Command;

				private static ValueTask HandleAsync(Command command, CancellationToken token)
				{
					_ = command;
					_ = token;
					return default;
				}
			}

			[Handler]
			[Callback("item")]
			public static partial class Item
			{
				public sealed record Command(string Id);

				private static ValueTask HandleAsync(Command command, CancellationToken token)
				{
					_ = command;
					_ = token;
					return default;
				}
			}
			""";

		var generated = GeneratorTestHelper.GetVexelGeneratedSource(GeneratorTestHelper.RunGenerators(source));
		await Verify(generated);
	}

	[Fact]
	public async Task Inline_query_and_chosen_inline_result_binders()
	{
		const string source = """
			using System.Threading;
			using System.Threading.Tasks;
			using Immediate.Handlers.Shared;
			using Vexel.Telegram.Handlers.Attributes;

			namespace Demo;

			[Handler]
			[InlineQuery("search")]
			public static partial class SearchInline
			{
				public sealed record Query(string Text);

				private static ValueTask HandleAsync(Query query, CancellationToken token)
				{
					_ = query;
					_ = token;
					return default;
				}
			}

			[Handler]
			[InlineQuery]
			public static partial class DefaultInline
			{
				public sealed record Query(string Text);

				private static ValueTask HandleAsync(Query query, CancellationToken token)
				{
					_ = query;
					_ = token;
					return default;
				}
			}

			[Handler]
			[ChosenInlineResult("item")]
			public static partial class ItemChosen
			{
				public sealed record Command(string Id);

				private static ValueTask HandleAsync(Command command, CancellationToken token)
				{
					_ = command;
					_ = token;
					return default;
				}
			}
			""";

		var generated = GeneratorTestHelper.GetVexelGeneratedSource(GeneratorTestHelper.RunGenerators(source));
		await Verify(generated);
	}

	[Fact]
	public async Task Flow_only_handler_emits_step_map_entry()
	{
		const string source = """
			using System.Threading;
			using System.Threading.Tasks;
			using Immediate.Handlers.Shared;

			namespace Demo;

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

		var generated = GeneratorTestHelper.GetVexelGeneratedSource(GeneratorTestHelper.RunGenerators(source));
		await Verify(generated);
	}

	[Fact]
	public async Task On_handlers_emit_sorted_dispatch_arrays()
	{
		// Registered out of FQ-name order; emission must sort Alpha before Zed.
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

				private static ValueTask HandleAsync(Command command, CancellationToken token)
				{
					_ = command;
					_ = token;
					return default;
				}
			}

			[Handler]
			[OnMessage]
			public static partial class ZedObserver
			{
				public sealed record Command;

				private static ValueTask HandleAsync(Command command, CancellationToken token)
				{
					_ = command;
					_ = token;
					return default;
				}
			}

			[Handler]
			[OnMessage]
			public static partial class AlphaObserver
			{
				public sealed record Command;

				private static ValueTask HandleAsync(Command command, CancellationToken token)
				{
					_ = command;
					_ = token;
					return default;
				}
			}

			[Handler]
			[OnCallbackQuery]
			public static partial class CallbackObserver
			{
				public sealed record Command;

				private static ValueTask HandleAsync(Command command, CancellationToken token)
				{
					_ = command;
					_ = token;
					return default;
				}
			}
			""";

		var generated = GeneratorTestHelper.GetVexelGeneratedSource(GeneratorTestHelper.RunGenerators(source));
		await Verify(generated);
	}
}
