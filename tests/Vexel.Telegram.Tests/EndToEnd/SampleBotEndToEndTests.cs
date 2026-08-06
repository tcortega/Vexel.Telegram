using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Vexel.Telegram.Client;
using Vexel.Telegram.Handlers.DependencyInjection;
using Vexel.Telegram.Handlers.Routing;
using Vexel.Telegram.Tests.Generators;
using Xunit.Abstractions;

namespace Vexel.Telegram.Tests.EndToEnd;

/// <summary>
/// End-to-end for the shipped sample bot (<c>samples/Vexel.Telegram.Sample</c>): its own handler
/// files are read off disk, compiled with the Immediate and Vexel generators, registered with the
/// three calls its <c>Program.cs</c> makes, and run in a real host against a fake Telegram Bot API.
/// A user then holds the session the sample README advertises - commands, callbacks, flow, inline,
/// chosen inline result and <c>[On*]</c> observers - and the assertions are on what Telegram saw.
/// </summary>
public sealed class SampleBotEndToEndTests(ITestOutputHelper output)
{
	private const string Token = "424242:AAHkQqL2b0zGf4nZ0K2QYQ0F0uSj1sM6Q8w";
	private const long ChatId = 100;

	/// <summary>Assembly name drives the generated method names the sample's Program.cs calls.</summary>
	private const string SampleAssemblyName = "Vexel.Telegram.Sample";

	/// <summary>Message id the fake API hands back for the first message the bot sends.</summary>
	private const int FirstBotMessageId = 9001;

	/// <summary>
	/// The sample's registration calls 2 and 3, verbatim from its <c>Program.cs</c>, compiled into
	/// the sample assembly so the generated method names themselves are under test.
	/// </summary>
	private const string SampleComposition = """
		using Microsoft.Extensions.DependencyInjection;

		namespace Vexel.Telegram.Sample;

		public static class SampleRegistration
		{
			public static IServiceCollection AddSampleBot(IServiceCollection services) =>
				services
					.AddVexelTelegramSampleHandlers()
					.AddVexelTelegramSampleTelegram();
		}
		""";

	[Fact]
	public async Task Sample_bot_serves_commands_callbacks_flow_inline_and_observers()
	{
		var transcript = new Transcript();

		// The sample's real source files, compiled the way its own csproj compiles them.
		var (sources, files) = LoadSampleHandlerSources();
		var botAssembly = GeneratorTestHelper.EmitBotAssembly(sources, SampleAssemblyName, out var generatedRoutes);
		transcript.Write(
			$"compiled the sample's own handler files ({string.Join(", ", files)}) with Immediate + Vexel generators");

		await using var api = new FakeTelegramBotApi(transcript.Write);
		api.Start();
		transcript.Write($"fake Telegram Bot API listening on {api.BaseAddress} (bot username @vexel_bot)");

		using var host = BuildHost(api.BaseAddress, transcript, botAssembly);
		await host.StartAsync();
		await WaitForAsync(() => api.GetUpdatesCalls >= 2);

		var router = host.Services.GetRequiredService<TelegramRouter>();
		var chat = new List<ChatLine>();
		var turns = new List<TurnNote>();
		var updateId = 700;

		// Everything the sample README tells a user to try, in one session.
		async Task TurnAsync(string what, string proves, Func<int, Task> send, params string[] expected)
		{
			updateId++;
			transcript.Write($"user {what}");
			chat.Add(new ChatLine("user", what));

			var before = api.OutboundRequests.Count;
			await send(updateId);

			if (expected.Length > 0)
			{
				await WaitForAsync(() => expected.All(
					e => api.OutboundCalls.Skip(before).Any(c => c.Contains(e, StringComparison.Ordinal))));
			}
			else
			{
				// Silence has to be waited out: wait until the update was actually handed over, then
				// give a handler the time a reply would have taken.
				await WaitForAsync(() => Delivered(api, updateId));
				await Task.Delay(400);
				Assert.Equal(before, api.OutboundRequests.Count);
			}

			var produced = api.OutboundRequests.Skip(before).ToArray();
			chat.AddRange(produced.Select(static call => RenderBotCall(call.Method, call.Body)));
			if (produced.Length == 0)
			{
				chat.Add(new ChatLine("bot", "(silence - nothing sent to the chat)"));
			}

			turns.Add(new TurnNote(
				what,
				produced.Length == 0
					? "_(silence)_"
					: string.Join("<br>", produced.Select(static c => Escape(RenderBotCall(c.Method, c.Body).Text))),
				proves));
		}

		Task TypeAsync(string text, string proves, bool isCommand, params string[] expected) =>
			TurnAsync(
				$"types \"{text}\"",
				proves,
				id =>
				{
					api.Enqueue(isCommand
						? FakeTelegramBotApi.CommandUpdate(id, ChatId, text)
						: FakeTelegramBotApi.MessageUpdate(id, ChatId, text));
					return Task.CompletedTask;
				},
				expected);

		Task TapAsync(string label, string data, string proves, params string[] expected) =>
			TurnAsync(
				$"taps [{label}] (callback_data \"{data}\")",
				proves,
				id =>
				{
					api.Enqueue(FakeTelegramBotApi.CallbackQueryUpdate(
						id,
						ChatId,
						FirstBotMessageId,
						$"cb-{id}",
						data));
					return Task.CompletedTask;
				},
				expected);

		Task InlineAsync(string query, string proves, params string[] expected) =>
			TurnAsync(
				$"types \"@vexel_bot {query}\" in some chat",
				proves,
				id =>
				{
					api.Enqueue(FakeTelegramBotApi.InlineQueryUpdate(id, ChatId, $"iq-{id}", query));
					return Task.CompletedTask;
				},
				expected);

		// 1. Commands + the button-first menu.
		await TypeAsync(
			"/start",
			"`[Handler]` + `[Command(\"start\")]` sends the menu keyboard the sample builds",
			isCommand: true,
			"Vexel.Telegram v2 sample",
			"callback_data=\"ping\"",
			"switch_inline_query=\"search \"");

		await TypeAsync(
			"/ping",
			"empty request record: `[Command(\"ping\")]` with no arguments",
			isCommand: true,
			"text=\"Pong!\"");

		await TypeAsync(
			"/echo hi there",
			"single-string request binds the rest of the command line",
			isCommand: true,
			"text=\"You said: hi there\"");

		await TypeAsync(
			"/help",
			"the help command every sample surface is listed in",
			isCommand: true,
			"What this sample shows");

		await TypeAsync(
			"/inline",
			"switch-inline buttons the keyboard builder emits, for trying inline mode",
			isCommand: true,
			"*Inline mode*",
			"switch_inline_query=\"search kittens\"");

		// 2. Callbacks, including a bound suffix.
		await TapAsync(
			"Ping",
			"ping",
			"`[Callback(\"ping\")]`: alert answer plus an edit of the bot's own message",
			"show_alert=true",
			"editMessageText");

		await TapAsync(
			"Colors",
			"colors",
			"callback edits the message into a second keyboard whose data carries suffixes",
			"callback_data=\"pick_color|red\"");

		await TapAsync(
			"Red",
			"pick_color|red",
			"`[Callback(\"pick_color\")]` binds the suffix after `|` to the request string",
			"Selected red",
			"picked *red*");

		// 3. Multi-turn flow.
		await TypeAsync(
			"/signup",
			"command arms the first flow step with `flow.PromptAsync<CollectName.Command>()`",
			isCommand: true,
			"What's your name?");

		await TypeAsync(
			"Ada Lovelace",
			"plain text goes to the armed step, which saves a draft and arms the next one",
			isCommand: false,
			"Nice to meet you, Ada Lovelace. How old are you?");

		await TypeAsync(
			"/menu",
			"commands still win over an armed step, so /menu works mid-signup",
			isCommand: true,
			"Vexel.Telegram v2 sample");

		await TypeAsync(
			"twelve-ish",
			"the step threw: generic retry line, and the step stays armed (B3)",
			isCommand: false,
			"Something went wrong - try again, or /cancel.");

		await TypeAsync(
			"36",
			"retry succeeds, the draft survived the throw, and the flow auto-completes",
			isCommand: false,
			"Registered Ada Lovelace, age 36. Flow complete.");

		// 4. Flow from the menu button, then the built-in /cancel.
		await TapAsync(
			"Sign up",
			"signup",
			"the same flow starts from a callback button",
			"Starting signup…",
			"What's your name?");

		await TypeAsync(
			"/nope",
			"the README pitfall: an unknown command mid-flow is a command miss, never step input",
			isCommand: true);

		await TypeAsync(
			"/cancel",
			"built-in /cancel clears the armed step (the sample defines no /cancel of its own)",
			isCommand: true,
			"text=\"Cancelled.\"");

		await TypeAsync(
			"Ada again",
			"nothing armed after cancel, so plain text is unrouted and the bot stays quiet",
			isCommand: false);

		// 5. Inline mode.
		await InlineAsync(
			"search cats",
			"`[InlineQuery(\"search\")]`: first token routes, the remainder binds as the term",
			"item|cats -> \"Search: cats\"",
			"item|cats-docs -> \"Docs hit for cats\"");

		await InlineAsync(
			"weather london",
			"unmatched first token falls back to the empty-trigger `[InlineQuery]` default",
			"item|default -> \"Default inline handler\"");

		await TurnAsync(
			"picks the \"Search: cats\" result out of the inline list",
			"`[ChosenInlineResult(\"item\")]` binds the suffix after `|` and logs the pick",
			id =>
			{
				api.Enqueue(FakeTelegramBotApi.ChosenInlineResultUpdate(
					id,
					ChatId,
					"item|cats",
					"search cats",
					"inline-msg-1"));
				return Task.CompletedTask;
			});

		// 6. On* observers: routed handlers ran above; this one only proves the fan-out is silent.
		await TypeAsync(
			"just chatting",
			"no route matches, so only the `[OnMessage]` observer runs - it logs, it does not reply",
			isCommand: false);

		await host.StopAsync();
		transcript.Write("host stopped");

		var calls = api.OutboundCalls;

		WriteChat(output, chat);
		output.WriteLine(string.Empty);
		output.WriteLine("Bot -> Telegram Bot API calls, as the Telegram side saw them:");
		foreach (var call in calls)
		{
			output.WriteLine($"  {call}");
		}

		var observerLines = transcript.Lines
			.Where(static l => l.Contains("after routed leg", StringComparison.Ordinal)
				|| l.Contains("Chosen inline result from", StringComparison.Ordinal))
			.ToArray();

		_ = EvidenceWriter.TryWrite(
			"sample-bot-session-e2e.md",
			BuildEvidence(files, generatedRoutes, chat, turns, observerLines, transcript, calls, router.CommandMetadata));

		// --- The sample's advertised surfaces, asserted on the wire ---

		// Commands the sample publishes to the BotFather menu at host start.
		Assert.Equal(
			["echo", "help", "inline", "menu", "ping", "signup", "start"],
			[.. router.CommandMetadata.Select(static c => c.Name).Order(StringComparer.Ordinal)]);
		Assert.Contains(
			calls,
			static c => c.StartsWith("setMyCommands", StringComparison.Ordinal)
				&& c.Contains("/ping - \"Check that the bot is alive\"", StringComparison.Ordinal));

		// Callback answers are first-wins: the app's own text is what the user sees, and the
		// fail-closed obligation does not answer a second time.
		var answers = calls.Where(static c => c.StartsWith("answerCallbackQuery", StringComparison.Ordinal)).ToArray();
		Assert.Contains(answers, static a => a.Contains("text=\"Pong!\"", StringComparison.Ordinal)
			&& a.Contains("show_alert=true", StringComparison.Ordinal));
		Assert.Contains(answers, static a => a.Contains("text=\"Selected red\"", StringComparison.Ordinal));
		Assert.Equal(4, answers.Length);

		// User-supplied text reaches Markdown-parsed messages escaped (MarkdownText.Escape).
		Assert.Contains(calls, static c => c.Contains("chat100 picked *red*.", StringComparison.Ordinal));

		// Inline answers carry the sample's result ids, which are what routes the chosen result.
		var inlineAnswers = calls.Where(static c => c.StartsWith("answerInlineQuery", StringComparison.Ordinal)).ToArray();
		Assert.Equal(2, inlineAnswers.Length);
		Assert.All(inlineAnswers, static a => Assert.Contains("cache_time=0", a, StringComparison.Ordinal));

		// On* observers ran for every update kind, after the routed leg.
		Assert.True(transcript.Contains("OnMessage after routed leg"));
		Assert.True(transcript.Contains("OnCallbackQuery after routed leg"));
		Assert.True(transcript.Contains("OnInlineQuery after routed leg"));
		Assert.True(transcript.Contains("Chosen inline result from"));

		// Ordering: the routed reply is on the wire before the observer that watched it logs.
		Assert.True(
			transcript.IndexOf("text=\"Pong!\"") < transcript.IndexOf("OnMessage after routed leg: chat=100 user=100 text=/ping"),
			"the [OnMessage] observer must run after the routed /ping handler replied");

		// The observers only log; the two unrouted turns produced no chat traffic at all.
		Assert.DoesNotContain(calls, static c => c.Contains("just chatting", StringComparison.Ordinal));
	}

	/// <summary>Reads the sample bot's own handler sources so the test can never drift from them.</summary>
	private static (IReadOnlyList<string> Sources, IReadOnlyList<string> Files) LoadSampleHandlerSources(
		[CallerFilePath] string testFilePath = "")
	{
		// tests/Vexel.Telegram.Tests/EndToEnd/<this file> -> repository root
		var repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFilePath)!, "..", "..", ".."));
		var handlersDirectory = Path.Combine(repositoryRoot, "samples", "Vexel.Telegram.Sample", "Handlers");

		Assert.True(Directory.Exists(handlersDirectory), $"Sample handlers not found at {handlersDirectory}");

		var paths = Directory.GetFiles(handlersDirectory, "*.cs").Order(StringComparer.Ordinal).ToArray();
		Assert.NotEmpty(paths);

		// The sample's Program.cs is top-level statements, so it cannot be compiled into a library;
		// this stands in for its registration calls, and the assertion below keeps the two in step.
		var program = File.ReadAllText(
			Path.Combine(repositoryRoot, "samples", "Vexel.Telegram.Sample", "Program.cs"));
		foreach (var call in (string[])
			["AddTelegramBot(", "AddVexelTelegramSampleHandlers()", "AddVexelTelegramSampleTelegram()"])
		{
			Assert.Contains(call, program, StringComparison.Ordinal);
		}

		// The sample compiles with ImplicitUsings; a hand-built compilation has to supply them.
		var sources = new List<string>
		{
			"""
			global using System;
			global using System.Collections.Generic;
			global using System.IO;
			global using System.Linq;
			global using System.Net.Http;
			global using System.Threading;
			global using System.Threading.Tasks;
			""",
			SampleComposition,
		};
		sources.AddRange(paths.Select(File.ReadAllText));

		return (sources, [.. paths.Select(Path.GetFileName)!]);
	}

	/// <summary>True once the fake server has handed update <paramref name="id"/> to the bot.</summary>
	private static bool Delivered(FakeTelegramBotApi api, int id) =>
		api.Requests.Any(r => r.Contains($",{id}]", StringComparison.Ordinal)
			|| r.EndsWith($"[{id}]", StringComparison.Ordinal));

	/// <summary>Renders one bot-initiated API call the way the Telegram client shows it.</summary>
	private static ChatLine RenderBotCall(string method, string body)
	{
		var request = JsonNode.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body) as JsonObject ?? [];
		var text = request["text"]?.GetValue<string>() ?? string.Empty;
		var buttons = DescribeButtons(request["reply_markup"]?["inline_keyboard"] as JsonArray);

		return method switch
		{
			"sendMessage" => new ChatLine("bot", text + buttons),
			"editMessageText" => new ChatLine("bot-edit", text + buttons),
			"answerCallbackQuery" when request["text"] is not null => new ChatLine(
				request["show_alert"]?.GetValue<bool>() == true ? "alert" : "toast",
				text),
			"answerCallbackQuery" => new ChatLine("ack", "(button spinner stops, no popup)"),
			"answerInlineQuery" => new ChatLine(
				"inline",
				string.Join(
					" | ",
					(request["results"] as JsonArray ?? []).OfType<JsonObject>()
						.Select(static r => r["title"]?.GetValue<string>() ?? string.Empty))),
			"setMyCommands" => new ChatLine(
				"menu",
				string.Join(
					", ",
					(request["commands"] as JsonArray ?? []).OfType<JsonObject>()
						.Select(static c => "/" + (c["command"]?.GetValue<string>() ?? string.Empty)))),
			_ => new ChatLine("api", method),
		};
	}

	private static string DescribeButtons(JsonArray? rows)
	{
		if (rows is null)
		{
			return string.Empty;
		}

		var lines = rows
			.OfType<JsonArray>()
			.Select(static row => "[ " + string.Join(" ] [ ", row.OfType<JsonObject>()
				.Select(static b => b["text"]?.GetValue<string>() ?? string.Empty)) + " ]");

		return Environment.NewLine + string.Join(Environment.NewLine, lines);
	}

	private static void WriteChat(ITestOutputHelper output, IEnumerable<ChatLine> chat)
	{
		output.WriteLine("Chat as the user sees it:");
		foreach (var line in chat)
		{
			output.WriteLine(Render(line));
		}
	}

	private static string Render(ChatLine line)
	{
		var prefix = line.Speaker switch
		{
			"user" => "user  >  ",
			"bot" => "  bot <  ",
			"bot-edit" => "  bot <  (edits its message) ",
			"alert" => "  bot <  [popup alert] ",
			"toast" => "  bot <  [toast] ",
			"ack" => "  bot <  ",
			"inline" => "  bot <  [inline results] ",
			"menu" => "  bot <  [command menu published] ",
			_ => "         ",
		};

		// Multi-line message bodies stay readable under the speaker gutter.
		return prefix + line.Text.ReplaceLineEndings(Environment.NewLine + "         ");
	}

	private static string Escape(string value) =>
		value.ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal).Trim();

	private static string BuildEvidence(
		IReadOnlyList<string> files,
		string generatedRoutes,
		IReadOnlyList<ChatLine> chat,
		IReadOnlyList<TurnNote> turns,
		IReadOnlyList<string> observerLines,
		Transcript transcript,
		IReadOnlyList<string> outboundCalls,
		IReadOnlyList<BotCommandDescriptor> commandMetadata)
	{
		var writer = new StringBuilder();

		writer.AppendLine("# Vexel.Telegram v2 sample bot, end to end");
		writer.AppendLine();
		writer.AppendLine(
			"The sample's own handler files (`samples/Vexel.Telegram.Sample/Handlers/`: "
			+ string.Join(", ", files.Select(static f => $"`{f}`"))
			+ ") are read off disk, compiled with the Immediate and Vexel generators, registered with");
		writer.AppendLine(
			"the same three calls `Program.cs` makes, and run in a real host against a fake Telegram");
		writer.AppendLine("Bot API server over HTTP. Every line below is a real message on the wire.");
		writer.AppendLine();

		writer.AppendLine("## 1. Wiring under test (the three calls from `Program.cs`)");
		writer.AppendLine();
		writer.AppendLine("```csharp");
		writer.AppendLine("builder.Services.AddTelegramBot(_ => token);        // client + host + contexts + Feedback + Flow + router");
		writer.AppendLine("builder.Services.AddVexelTelegramSampleHandlers();  // Immediate.Handlers");
		writer.AppendLine("builder.Services.AddVexelTelegramSampleTelegram();  // Vexel-generated routes / flow steps / On*");
		writer.AppendLine("```");
		writer.AppendLine();

		writer.AppendLine("## 2. The chat, as the user sees it");
		writer.AppendLine();
		writer.AppendLine("```text");
		foreach (var line in chat)
		{
			writer.AppendLine(Render(line));
		}

		writer.AppendLine("```");
		writer.AppendLine();

		writer.AppendLine("### What each turn proves");
		writer.AppendLine();
		writer.AppendLine("| User does | Bot sends | Surface demonstrated |");
		writer.AppendLine("| --- | --- | --- |");
		foreach (var turn in turns)
		{
			writer.AppendLine(CultureInfo.InvariantCulture, $"| {Escape(turn.What)} | {turn.Sent} | {Escape(turn.Proves)} |");
		}

		writer.AppendLine();

		writer.AppendLine("## 3. Command menu the sample publishes at start (`setMyCommands`)");
		writer.AppendLine();
		writer.AppendLine("| Command | Description |");
		writer.AppendLine("| --- | --- |");
		foreach (var command in commandMetadata)
		{
			writer.AppendLine(CultureInfo.InvariantCulture, $"| `/{command.Name}` | {command.Description} |");
		}

		writer.AppendLine();

		writer.AppendLine("## 4. `[On*]` observers (they log, they never reply)");
		writer.AppendLine();
		writer.AppendLine("```text");
		foreach (var line in observerLines)
		{
			writer.AppendLine(line);
		}

		writer.AppendLine("```");
		writer.AppendLine();

		writer.AppendLine("## 5. Route table the Vexel generator emitted for the sample (`Vexel.Telegram.Routes.g.cs`)");
		writer.AppendLine();
		writer.AppendLine("```csharp");
		writer.AppendLine(generatedRoutes.TrimEnd());
		writer.AppendLine("```");
		writer.AppendLine();

		writer.AppendLine("## 6. Bot -> Telegram Bot API calls, in order");
		writer.AppendLine();
		writer.AppendLine("```text");
		foreach (var call in outboundCalls)
		{
			writer.AppendLine(call);
		}

		writer.AppendLine("```");
		writer.AppendLine();

		writer.AppendLine("## 7. Live session log");
		writer.AppendLine();
		writer.AppendLine("```text");
		foreach (var line in transcript.Lines)
		{
			writer.AppendLine(line);
		}

		writer.AppendLine("```");
		writer.AppendLine();

		return writer.ToString();
	}

	private static IHost BuildHost(string apiAddress, Transcript transcript, Assembly botAssembly)
	{
		var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

		_ = builder.Logging.SetMinimumLevel(LogLevel.Information);
		_ = builder.Services.AddSingleton<ILoggerProvider>(_ => new TranscriptLoggerProvider(transcript));

		_ = builder.Services.AddSingleton<ITelegramBotClient>(
			_ => new TelegramBotClient(new TelegramBotClientOptions(Token, apiAddress)));

		// The sample's three calls, in the sample's order. Calls 2 and 3 run inside the sample
		// assembly (SampleComposition below), because Immediate's generated overload takes a
		// `params ReadOnlySpan<string>` that reflection cannot bind.
		_ = builder.Services.AddTelegramBot(_ => Token);
		Invoke(botAssembly, "AddSampleBot", builder.Services);

		return builder.Build();
	}

	/// <summary>Calls a generated <c>IServiceCollection</c> extension method by name.</summary>
	private static void Invoke(Assembly assembly, string methodName, IServiceCollection services)
	{
		// Immediate also emits a `params ReadOnlySpan<string> tags` overload, which reflection cannot
		// call; take the plain one and let the binder fill in the optional arguments.
		var method = assembly.GetTypes()
			.SelectMany(static t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
			.Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
			.Where(static m => m.GetParameters().All(static p => !p.ParameterType.IsByRefLike))
			.OrderBy(static m => m.GetParameters().Length)
			.FirstOrDefault();

		Assert.True(
			method is not null,
			$"Generated method {methodName} was not emitted. Emitted: "
			+ string.Join(
				", ",
				assembly.GetTypes()
					.SelectMany(static t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
					.Select(static m => m.Name)
					.Where(static n => n.StartsWith("Add", StringComparison.Ordinal))
					.Distinct(StringComparer.Ordinal)));

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

	private sealed record ChatLine(string Speaker, string Text);

	private sealed record TurnNote(string What, string Sent, string Proves);
}
