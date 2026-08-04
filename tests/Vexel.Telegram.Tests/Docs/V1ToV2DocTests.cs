using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Vexel.Telegram.Handlers.DependencyInjection;
using Vexel.Telegram.Tests.EndToEnd;
using Vexel.Telegram.Tests.Generators;
using Xunit.Abstractions;

namespace Vexel.Telegram.Tests.Docs;

/// <summary>
/// Keeps <c>docs/v1-to-v2.md</c> honest. The migration guide is the first thing a v1 bot author
/// copies from, so its C# is lifted out of the Markdown verbatim, compiled with the real Immediate
/// and Vexel generators, and run in a host against a fake Telegram Bot API - and the concrete repo
/// claims it makes (diagnostic ids, package shape, links) are asserted against the repository.
/// A doc that drifts from the code fails here instead of in a migrator's editor.
/// </summary>
public sealed class V1ToV2DocTests(ITestOutputHelper output)
{
	private const string Token = "424242:AAHkQqL2b0zGf4nZ0K2QYQ0F0uSj1sM6Q8w";
	private const long ChatId = 100;

	/// <summary>Implicit usings the doc's snippets assume, as any real bot project has them.</summary>
	private const string GlobalUsings = """
		global using System;
		global using System.Collections.Generic;
		global using System.Linq;
		global using System.Threading;
		global using System.Threading.Tasks;
		""";

	[Fact]
	public async Task Doc_ping_snippet_compiles_and_answers_a_real_chat()
	{
		var doc = ReadDoc();
		var snippet = CSharpSnippetContaining(doc, "class Ping");

		var transcript = new Transcript();
		var botAssembly = GeneratorTestHelper.EmitBotAssembly(
			[GlobalUsings, Registration("DocPingBot"), snippet],
			"DocPingBot",
			out _);

		await using var api = new FakeTelegramBotApi(transcript.Write);
		api.Start();

		using var host = BuildHost(api.BaseAddress, transcript, botAssembly);
		await host.StartAsync();
		await WaitForAsync(() => api.GetUpdatesCalls >= 2);

		api.Enqueue(FakeTelegramBotApi.CommandUpdate(801, ChatId, "/ping"));
		await WaitForAsync(() => api.OutboundCalls.Any(
			static c => c.Contains("text=\"Pong!\"", StringComparison.Ordinal)));

		await host.StopAsync();

		var calls = api.OutboundCalls;
		foreach (var call in calls)
		{
			output.WriteLine(call);
		}

		// The doc also promises SetMyCommands is automatic from [Command] metadata.
		Assert.Contains(
			calls,
			static c => c.StartsWith("setMyCommands", StringComparison.Ordinal)
				&& c.Contains("/ping - \"Check that the bot is alive\"", StringComparison.Ordinal));

		_ = EvidenceWriter.TryWrite(
			"docs-v1-to-v2-snippets-e2e.md",
			BuildEvidence(snippet, calls));
	}

	[Fact]
	public async Task Doc_flow_snippet_compiles_and_drives_a_multi_turn_conversation()
	{
		var doc = ReadDoc();
		var snippet = CSharpSnippetContaining(doc, "PromptAsync<CollectName.Command>");

		// The doc shows three fragments of one conversation (arm, step body, cancel). They are
		// spliced into real handlers here character for character, so every Flow member and
		// overload the guide advertises has to exist and behave for this test to pass.
		var arm = SnippetLine(snippet, "PromptAsync<CollectName.Command>");
		var stepBody = SnippetBetween(snippet, "GetDraftAsync<SignupDraft>", "PromptAsync<CollectAge.Command>");
		var cancel = SnippetLine(snippet, "CancelAsync");

		var botSource = $$"""
			using Immediate.Handlers.Shared;
			using Vexel.Telegram.Handlers;
			using Vexel.Telegram.Handlers.Attributes;

			namespace DocFlowBot;

			public sealed class SignupDraft
			{
				public string Name { get; set; } = "";

				public int Age { get; set; }
			}

			[Handler]
			[Command("signup", Description = "Doc flow snippet")]
			public static partial class Signup
			{
				public sealed record Command;

				private static async ValueTask HandleAsync(Command command, Flow flow, Feedback feedback, CancellationToken token)
				{
					{{arm}}
					_ = await feedback.ReplyAsync("What's your name?", cancellationToken: token);
				}
			}

			[Handler]
			public static partial class CollectName
			{
				public sealed record Command(string Name);

				private static async ValueTask HandleAsync(Command command, Flow flow, Feedback feedback, CancellationToken token)
				{
					{{stepBody}}
					_ = await feedback.ReplyAsync($"Hi {command.Name.Trim()}, how old are you?", cancellationToken: token);
				}
			}

			[Handler]
			public static partial class CollectAge
			{
				public sealed record Command(string Age);

				private static async ValueTask HandleAsync(Command command, Flow flow, Feedback feedback, CancellationToken token)
				{
					var draft = await flow.GetDraftAsync<SignupDraft>(token) ?? new SignupDraft();
					_ = await feedback.ReplyAsync(
						$"Registered {draft.Name}, age {command.Age.Trim()}. Flow complete.",
						cancellationToken: token);
				}
			}

			[Handler]
			[Command("bail", Description = "Explicit cancel from the doc snippet")]
			public static partial class Bail
			{
				public sealed record Command;

				private static async ValueTask HandleAsync(Command command, Flow flow, Feedback feedback, CancellationToken token)
				{
					{{cancel}}
					_ = await feedback.ReplyAsync("Bailed.", cancellationToken: token);
				}
			}
			""";

		var transcript = new Transcript();
		var botAssembly = GeneratorTestHelper.EmitBotAssembly(
			[GlobalUsings, Registration("DocFlowBot"), botSource],
			"DocFlowBot",
			out _);

		await using var api = new FakeTelegramBotApi(transcript.Write);
		api.Start();

		using var host = BuildHost(api.BaseAddress, transcript, botAssembly);
		await host.StartAsync();
		await WaitForAsync(() => api.GetUpdatesCalls >= 2);

		var id = 850;
		async Task SayAsync(string text, bool isCommand, string expected)
		{
			id++;
			api.Enqueue(isCommand
				? FakeTelegramBotApi.CommandUpdate(id, ChatId, text)
				: FakeTelegramBotApi.MessageUpdate(id, ChatId, text));

			await WaitForAsync(() => api.OutboundCalls.Any(c => c.Contains(expected, StringComparison.Ordinal)));
		}

		await SayAsync("/signup", isCommand: true, "What's your name?");
		await SayAsync("Ada Lovelace", isCommand: false, "how old are you?");
		await SayAsync("36", isCommand: false, "Registered Ada Lovelace, age 36. Flow complete.");

		// The doc's "leading-/ text is still command-routed first" and CancelAsync claims.
		await SayAsync("/signup", isCommand: true, "What's your name?");
		await SayAsync("/bail", isCommand: true, "Bailed.");

		var beforeUnrouted = api.OutboundRequests.Count;
		id++;
		api.Enqueue(FakeTelegramBotApi.MessageUpdate(id, ChatId, "Ada again"));
		await Task.Delay(500);
		Assert.Equal(beforeUnrouted, api.OutboundRequests.Count);

		await host.StopAsync();

		foreach (var call in api.OutboundCalls)
		{
			output.WriteLine(call);
		}

		_ = EvidenceWriter.TryWrite(
			"docs-v1-to-v2-flow-snippet-e2e.md",
			BuildFlowEvidence(snippet, api.OutboundCalls));
	}

	[Fact]
	public void Doc_repository_claims_still_hold()
	{
		var root = RepositoryRoot();
		var doc = ReadDoc();

		// 1. Every relative Markdown link resolves on disk.
		foreach (Match link in Regex.Matches(doc, @"\]\((?<path>\.[^)]+)\)", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(5)))
		{
			var target = Path.GetFullPath(Path.Combine(root, "docs", link.Groups["path"].Value));
			Assert.True(
				File.Exists(target) || Directory.Exists(target),
				$"docs/v1-to-v2.md links to {link.Groups["path"].Value}, which does not exist.");
		}

		// 2. Every diagnostic id the doc points migrators at is a real, released rule id.
		var releases = File.ReadAllText(
			Path.Combine(root, "src", "Vexel.Telegram.Generators", "AnalyzerReleases.Unshipped.md"));
		foreach (var id in DiagnosticIdsMentioned(doc))
		{
			Assert.Contains(id, releases, StringComparison.Ordinal);
		}

		// 3. "generator ships as an analyzer ... not published alone" and the T15 caveat.
		Assert.Contains(
			"<IsPackable>false</IsPackable>",
			File.ReadAllText(Path.Combine(root, "src", "Vexel.Telegram.Generators", "Vexel.Telegram.Generators.csproj")),
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"analyzers/dotnet/cs",
			File.ReadAllText(Path.Combine(root, "src", "Vexel.Telegram.Handlers", "Vexel.Telegram.Handlers.csproj")),
			StringComparison.Ordinal);

		// 4. "metapackage of Client + Handlers + Hosting (excludes AspNetCore)".
		var metapackage = File.ReadAllText(Path.Combine(root, "src", "Vexel.Telegram", "Vexel.Telegram.csproj"));
		Assert.DoesNotContain("Vexel.Telegram.AspNetCore", metapackage, StringComparison.Ordinal);
		foreach (var package in (string[])["Vexel.Telegram.Client", "Vexel.Telegram.Handlers", "Vexel.Telegram.Hosting"])
		{
			Assert.Contains(package, metapackage, StringComparison.Ordinal);
		}

		// 5. "Hosting ... no ASP.NET Core framework reference"; AspNetCore owns the sole one.
		Assert.DoesNotContain(
			"Microsoft.AspNetCore.App",
			File.ReadAllText(Path.Combine(root, "src", "Vexel.Telegram.Hosting", "Vexel.Telegram.Hosting.csproj")),
			StringComparison.Ordinal);
		Assert.Contains(
			"Microsoft.AspNetCore.App",
			File.ReadAllText(Path.Combine(root, "src", "Vexel.Telegram.AspNetCore", "Vexel.Telegram.AspNetCore.csproj")),
			StringComparison.Ordinal);

		// 6. The honest testing table: the real test-DC suite does not exist on this branch yet.
		Assert.False(
			Directory.Exists(Path.Combine(root, "tests", "Vexel.Telegram.E2E")),
			"docs/v1-to-v2.md says tests/Vexel.Telegram.E2E does not exist yet; it now does.");
	}

	private static IEnumerable<string> DiagnosticIdsMentioned(string doc) =>
		Regex.Matches(doc, @"VEX\d{4}", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(5))
			.Select(static m => m.Value)
			.Distinct(StringComparer.Ordinal);

	private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string testFilePath = "") =>
		// tests/Vexel.Telegram.Tests/Docs/<this file> -> repository root
		Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFilePath)!, "..", "..", ".."));

	private static string ReadDoc() =>
		File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "v1-to-v2.md"));

	/// <summary>Lifts the fenced C# block that contains <paramref name="marker"/> out of the doc.</summary>
	private static string CSharpSnippetContaining(string doc, string marker)
	{
		var snippet = Regex
			.Matches(
				doc,
				"```csharp\r?\n(?<code>.*?)```",
				RegexOptions.Singleline | RegexOptions.ExplicitCapture,
				TimeSpan.FromSeconds(5))
			.Select(static m => m.Groups["code"].Value)
			.FirstOrDefault(s => s.Contains(marker, StringComparison.Ordinal));

		Assert.True(snippet is not null, $"docs/v1-to-v2.md no longer has a C# snippet containing '{marker}'.");
		return snippet;
	}

	/// <summary>The single doc line containing <paramref name="marker"/>, trimmed of its indentation.</summary>
	private static string SnippetLine(string snippet, string marker)
	{
		var line = snippet
			.Split('\n')
			.Select(static l => l.Trim())
			.FirstOrDefault(l => l.Contains(marker, StringComparison.Ordinal));

		Assert.True(line is not null, $"Doc snippet no longer contains a line with '{marker}'.");

		// Drop the doc's trailing explanatory comment; the statement itself is what has to compile.
		var comment = line.IndexOf("//", StringComparison.Ordinal);
		return (comment < 0 ? line : line[..comment]).Trim();
	}

	/// <summary>The doc's consecutive statements from the <paramref name="first"/> line through <paramref name="last"/>.</summary>
	private static string SnippetBetween(string snippet, string first, string last)
	{
		var lines = snippet.Split('\n').Select(static l => l.Trim()).ToArray();
		var start = Array.FindIndex(lines, l => l.Contains(first, StringComparison.Ordinal));
		var end = Array.FindIndex(lines, l => l.Contains(last, StringComparison.Ordinal));

		Assert.True(start >= 0 && end >= start, $"Doc snippet no longer runs from '{first}' to '{last}'.");

		return string.Join(
			Environment.NewLine + "\t\t\t\t",
			lines[start..(end + 1)].Select(static l =>
			{
				var comment = l.IndexOf("//", StringComparison.Ordinal);
				return (comment < 0 ? l : l[..comment]).Trim();
			}));
	}

	/// <summary>The composition root the doc's own three-call wiring section describes.</summary>
	private static string Registration(string assemblyName) => $$"""
		using Microsoft.Extensions.DependencyInjection;

		namespace {{assemblyName}}.Composition;

		public static class DocRegistration
		{
			public static IServiceCollection AddDocBot(IServiceCollection services) =>
				services
					.Add{{assemblyName}}Handlers()
					.Add{{assemblyName}}Telegram();
		}
		""";

	private static IHost BuildHost(string apiAddress, Transcript transcript, Assembly botAssembly)
	{
		var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

		_ = builder.Logging.SetMinimumLevel(LogLevel.Information);
		_ = builder.Services.AddSingleton<ILoggerProvider>(_ => new TranscriptLoggerProvider(transcript));
		_ = builder.Services.AddSingleton<ITelegramBotClient>(
			_ => new TelegramBotClient(new TelegramBotClientOptions(Token, apiAddress)));

		// Exactly the doc's three-call wiring: AddTelegramBot, then the bot's own two generated calls.
		_ = builder.Services.AddTelegramBot(_ => Token);
		Invoke(botAssembly, "AddDocBot", builder.Services);

		return builder.Build();
	}

	private static void Invoke(Assembly assembly, string methodName, IServiceCollection services)
	{
		var method = assembly.GetTypes()
			.SelectMany(static t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
			.Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
			.Where(static m => m.GetParameters().All(static p => !p.ParameterType.IsByRefLike))
			.OrderBy(static m => m.GetParameters().Length)
			.FirstOrDefault();

		Assert.True(method is not null, $"Generated method {methodName} was not emitted.");

		object[] arguments =
		[
			services,
			.. Enumerable.Repeat(Type.Missing, method.GetParameters().Length - 1),
		];

		_ = method.Invoke(
			null,
			BindingFlags.OptionalParamBinding | BindingFlags.InvokeMethod,
			binder: Type.DefaultBinder,
			arguments,
			culture: CultureInfo.InvariantCulture);
	}

	private static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
	{
		var limit = timeout ?? TimeSpan.FromSeconds(20);
		var start = DateTime.UtcNow;
		while (!condition())
		{
			if (DateTime.UtcNow - start > limit)
			{
				throw new TimeoutException("Condition was not met within the allotted time.");
			}

			await Task.Delay(20);
		}
	}

	private static string BuildEvidence(string snippet, IReadOnlyList<string> calls)
	{
		var writer = new StringBuilder();

		writer.AppendLine("# `docs/v1-to-v2.md`: the guide's own snippet, run as a bot");
		writer.AppendLine();
		writer.AppendLine(
			"The handler below was lifted verbatim out of the migration guide's Markdown, compiled with");
		writer.AppendLine(
			"the Immediate + Vexel generators, and registered through the guide's own three-call wiring");
		writer.AppendLine("in a real host talking to a fake Telegram Bot API over HTTP.");
		writer.AppendLine();
		writer.AppendLine("## Snippet, exactly as `docs/v1-to-v2.md` prints it");
		writer.AppendLine();
		writer.AppendLine("```csharp");
		writer.Append(snippet);
		writer.AppendLine("```");
		writer.AppendLine();
		writer.AppendLine("## What Telegram saw");
		writer.AppendLine();
		writer.AppendLine("```text");
		writer.AppendLine("user  >  /ping");
		foreach (var call in calls)
		{
			writer.AppendLine(CultureInfo.InvariantCulture, $"  api <  {call}");
		}

		writer.AppendLine("```");
		writer.AppendLine();
		writer.AppendLine(
			"`setMyCommands` above is the guide's \"`SetMyCommands` runs automatically from `[Command]`");
		writer.AppendLine("metadata\" claim, on the wire, from the snippet's own `Description`.");

		return writer.ToString();
	}

	private static string BuildFlowEvidence(string snippet, IReadOnlyList<string> calls)
	{
		var writer = new StringBuilder();

		writer.AppendLine("# `docs/v1-to-v2.md`: the Flow snippet, run as a conversation");
		writer.AppendLine();
		writer.AppendLine(
			"The guide's Flow statements were spliced character for character into real handlers, so");
		writer.AppendLine(
			"`PromptAsync<TRequest>`, `GetDraftAsync`, `SetDraftAsync` and `CancelAsync` had to exist and");
		writer.AppendLine("behave as printed for this conversation to happen.");
		writer.AppendLine();
		writer.AppendLine("## Snippet, exactly as `docs/v1-to-v2.md` prints it");
		writer.AppendLine();
		writer.AppendLine("```csharp");
		writer.Append(snippet);
		writer.AppendLine("```");
		writer.AppendLine();
		writer.AppendLine("## The chat");
		writer.AppendLine();
		writer.AppendLine("```text");
		writer.AppendLine("user  >  /signup            (arms the first step)");
		writer.AppendLine("user  >  Ada Lovelace       (armed step: draft written, next step armed)");
		writer.AppendLine("user  >  36                 (success without re-arm -> auto-completes)");
		writer.AppendLine("user  >  /signup            (arms again)");
		writer.AppendLine("user  >  /bail              (command wins mid-flow; CancelAsync clears it)");
		writer.AppendLine("user  >  Ada again          (nothing armed -> unrouted, bot stays silent)");
		writer.AppendLine("```");
		writer.AppendLine();
		writer.AppendLine("Bot -> Telegram, as the Telegram side saw it:");
		writer.AppendLine();
		writer.AppendLine("```text");
		foreach (var call in calls)
		{
			writer.AppendLine(call);
		}

		writer.AppendLine("```");

		return writer.ToString();
	}
}
