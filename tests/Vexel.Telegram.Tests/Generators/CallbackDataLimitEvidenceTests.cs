using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Vexel.Telegram.Generators.Analyzers;
using Vexel.Telegram.Handlers.Keyboards;
using Xunit.Abstractions;

namespace Vexel.Telegram.Tests.Generators;

/// <summary>
/// The 64-byte callback_data rule as a bot author meets it: VEX0002 at build time on bad
/// <c>[Callback]</c> keys, the invalid route kept out of the generated table, and the keyboard
/// helpers refusing to emit oversized data at runtime.
/// </summary>
public sealed class CallbackDataLimitEvidenceTests(ITestOutputHelper output)
{
	private static readonly string s_brokenBotSource = $$"""
		using System.Threading;
		using System.Threading.Tasks;
		using Immediate.Handlers.Shared;
		using Vexel.Telegram.Handlers.Attributes;

		namespace DemoBot;

		// Fine: short key, well under the Telegram limit.
		[Handler]
		[Callback("confirm")]
		public static partial class Confirm
		{
			public sealed record Command(string OrderId);

			private static ValueTask HandleAsync(Command command, CancellationToken token) => default;
		}

		// VEX0002: 65 ASCII bytes of callback_data before any suffix is even added.
		[Handler]
		[Callback("{{new string('a', 65)}}")]
		public static partial class TooLong
		{
			public sealed record Command;

			private static ValueTask HandleAsync(Command command, CancellationToken token) => default;
		}

		// VEX0002: 22 euro signs are 66 UTF-8 bytes, though only 22 characters.
		[Handler]
		[Callback("{{new string('€', 22)}}")]
		public static partial class TooManyBytes
		{
			public sealed record Command;

			private static ValueTask HandleAsync(Command command, CancellationToken token) => default;
		}

		// VEX0002: '|' is the key/suffix separator, so it cannot appear in the key.
		[Handler]
		[Callback("bad|key")]
		public static partial class PipeInKey
		{
			public sealed record Command;

			private static ValueTask HandleAsync(Command command, CancellationToken token) => default;
		}

		// VEX0002: blank keys would send zero bytes of callback_data.
		[Handler]
		[Callback("   ")]
		public static partial class Blank
		{
			public sealed record Command;

			private static ValueTask HandleAsync(Command command, CancellationToken token) => default;
		}
		""";

	[Fact]
	public async Task Oversized_callback_data_is_rejected_at_build_time_and_at_runtime()
	{
		var diagnostics = await GeneratorTestHelper.RunAnalyzersAsync(
			s_brokenBotSource,
			new CallbackDataTooLongAnalyzer());

		var reported = diagnostics
			.OrderBy(static d => d.Location.GetLineSpan().StartLinePosition.Line)
			.Select(static d => string.Create(
				CultureInfo.InvariantCulture,
				$"{d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.Severity.ToString().ToLowerInvariant()} {d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}"))
			.ToArray();

		// The invalid keys must also stay out of the generated route table, so a build that somehow
		// suppresses the diagnostic still cannot register unreachable routes.
		var generatedRoutes = GeneratorTestHelper.GetVexelGeneratedSource(
			GeneratorTestHelper.RunGenerators(s_brokenBotSource, "DemoBot", out _));

		var runtimeRejections = new[]
		{
			Reject("suffix pushes data over the limit", static () =>
				new InlineKeyboardBuilder().AddCallbackButton("Confirm", "confirm", new string('x', 70))),
			Reject("64 bytes counted as UTF-8, not characters", static () =>
				CallbackData.Format(new string('€', 22))),
			Reject("'|' in the route key", static () => CallbackData.Format("bad|key")),
			Reject("blank route key", static () => CallbackData.Format("   ")),
		};

		foreach (var line in reported.Concat(runtimeRejections))
		{
			output.WriteLine(line);
		}

		_ = EvidenceWriter.TryWrite(
			"callback-data-64-byte-limit.md",
			BuildEvidence(reported, generatedRoutes, runtimeRejections));

		// One VEX0002 error per bad key, and none for the good one.
		Assert.Equal(4, diagnostics.Length);
		Assert.All(diagnostics, static d =>
		{
			Assert.Equal("VEX0002", d.Id);
			Assert.Equal(DiagnosticSeverity.Error, d.Severity);
		});

		Assert.Contains("\"confirm\"", generatedRoutes, StringComparison.Ordinal);
		Assert.DoesNotContain("bad|key", generatedRoutes, StringComparison.Ordinal);
		Assert.DoesNotContain(new string('a', 65), generatedRoutes, StringComparison.Ordinal);
		Assert.DoesNotContain(new string('€', 22), generatedRoutes, StringComparison.Ordinal);
	}

	/// <summary>Runs <paramref name="action"/> and renders the exception a bot author would get.</summary>
	private static string Reject(string what, Action action)
	{
		var ex = Assert.Throws<ArgumentException>(action);
		return $"{what}: {ex.GetType().Name}: {ex.Message.Split(" (Parameter")[0]}";
	}

	/// <summary>Pulls the emitted callback dictionary out of the generated route table.</summary>
	private static string CallbackTable(string generatedRoutes)
	{
		var lines = generatedRoutes.Split('\n').Select(static l => l.TrimEnd()).ToArray();
		var start = Array.FindIndex(lines, static l => l.Contains("var callbacks = ", StringComparison.Ordinal));
		Assert.True(start >= 0, "Generated route table has no callback dictionary.");

		var end = Array.FindIndex(lines, start, lines.Length - start, static l => l.Trim() is "};" or "});");
		Assert.True(end >= 0, "Callback dictionary is not terminated.");

		return string.Join(Environment.NewLine, lines[start..(end + 1)]);
	}

	private static string BuildEvidence(
		IReadOnlyList<string> reported,
		string generatedRoutes,
		IReadOnlyList<string> runtimeRejections)
	{
		var evidence = new StringBuilder();

		evidence.AppendLine("# The 64-byte callback_data limit");
		evidence.AppendLine();
		evidence.AppendLine(
			"Telegram caps `callback_data` at 64 UTF-8 bytes. Vexel enforces it twice: VEX0002 at build");
		evidence.AppendLine(
			"time on `[Callback]` keys, and the keyboard helpers at runtime on the data they emit.");
		evidence.AppendLine();

		evidence.AppendLine("## The (broken) bot source");
		evidence.AppendLine();
		evidence.AppendLine("```csharp");
		evidence.AppendLine(s_brokenBotSource);
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
		evidence.AppendLine();

		evidence.AppendLine("## Callback routes the generator emitted for that source");
		evidence.AppendLine();
		evidence.AppendLine(
			"Only the valid key reaches the route table; the rejected keys are never registered.");
		evidence.AppendLine();
		evidence.AppendLine("```csharp");
		evidence.AppendLine(CallbackTable(generatedRoutes));
		evidence.AppendLine("```");
		evidence.AppendLine();

		evidence.AppendLine("## Runtime: what the keyboard helpers do with oversized data");
		evidence.AppendLine();
		evidence.AppendLine("```text");
		foreach (var line in runtimeRejections)
		{
			evidence.AppendLine(line);
		}

		evidence.AppendLine("```");

		return evidence.ToString();
	}
}
