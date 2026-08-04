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
}
