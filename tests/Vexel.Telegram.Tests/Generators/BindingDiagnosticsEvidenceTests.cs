using System.Text;
using Microsoft.CodeAnalysis;
using Vexel.Telegram.Generators.Analyzers;
using Xunit.Abstractions;

namespace Vexel.Telegram.Tests.Generators;

/// <summary>
/// The four dual-attr / binding-convention analyzers as a bot author meets them: one file of
/// realistic mistakes, compiled, and the build output they produce.
/// </summary>
public sealed class BindingDiagnosticsEvidenceTests(ITestOutputHelper output)
{
	private const string BrokenBotSource = """
		using System.Threading;
		using System.Threading.Tasks;
		using Immediate.Handlers.Shared;
		using Vexel.Telegram.Handlers.Attributes;

		namespace DemoBot;

		// VEX0001: [Command] without the sibling [Handler].
		[Command("ping")]
		public static class Ping
		{
			public sealed record Command;
		}

		// VEX0003: not a legal Telegram command name.
		[Handler]
		[Command("Bad Name")]
		public static class Shout
		{
			public sealed record Command;

			private static ValueTask HandleAsync(Command command, CancellationToken token) => default;
		}

		// VEX0004: the request shape cannot be bound from command text.
		[Handler]
		[Command("tags")]
		public static class Tags
		{
			public sealed record Command(string[] Items);

			private static ValueTask HandleAsync(Command command, CancellationToken token) => default;
		}

		// VEX0005: two handlers claim the same command key.
		[Handler]
		[Command("status")]
		public static class StatusA
		{
			public sealed record Command;

			private static ValueTask HandleAsync(Command command, CancellationToken token) => default;
		}

		[Handler]
		[Command("status")]
		public static class StatusB
		{
			public sealed record Command;

			private static ValueTask HandleAsync(Command command, CancellationToken token) => default;
		}
		""";

	[Fact]
	public async Task Binding_convention_mistakes_report_as_build_diagnostics()
	{
		var diagnostics = await GeneratorTestHelper.RunAnalyzersAsync(
			BrokenBotSource,
			new MissingHandlerAttributeAnalyzer(),
			new InvalidCommandNameAnalyzer(),
			new UnbindableRequestAnalyzer(),
			new DuplicateRouteKeyAnalyzer());

		var reported = diagnostics
			.OrderBy(static d => d.Location.GetLineSpan().StartLinePosition.Line)
			.ThenBy(static d => d.Id, StringComparer.Ordinal)
			.Select(static d => string.Concat(
				d.Location.GetLineSpan().StartLinePosition.Line + 1,
				": ",
				d.Severity.ToString().ToLowerInvariant(),
				" ",
				d.Id,
				": ",
				d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)))
			.ToArray();

		foreach (var line in reported)
		{
			output.WriteLine(line);
		}

		var evidence = new StringBuilder();
		evidence.AppendLine("# Binding-convention diagnostics");
		evidence.AppendLine();
		evidence.AppendLine("What a bot author sees when the dual-attr / binding rules are broken.");
		evidence.AppendLine();
		evidence.AppendLine("## The (broken) bot source");
		evidence.AppendLine();
		evidence.AppendLine("```csharp");
		evidence.AppendLine(BrokenBotSource);
		evidence.AppendLine("```");
		evidence.AppendLine();
		evidence.AppendLine("## Build output (line: severity id: message)");
		evidence.AppendLine();
		evidence.AppendLine("```text");
		foreach (var line in reported)
		{
			evidence.AppendLine(line);
		}

		evidence.AppendLine("```");

		_ = EvidenceWriter.TryWrite("binding-diagnostics.md", evidence.ToString());

		// Every rule in the slice fires on the mistake it owns, as an error the build cannot ignore.
		Assert.Equal(
			["VEX0001", "VEX0003", "VEX0004", "VEX0005", "VEX0005"],
			[.. diagnostics.Select(static d => d.Id).OrderBy(static id => id, StringComparer.Ordinal)]);
		Assert.All(diagnostics, static d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
	}
}
