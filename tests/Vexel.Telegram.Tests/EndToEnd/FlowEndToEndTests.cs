using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Vexel.Telegram.Handlers;
using Vexel.Telegram.Handlers.DependencyInjection;
using Vexel.Telegram.Handlers.Routing;
using Vexel.Telegram.Tests.Generators;
using Xunit.Abstractions;

namespace Vexel.Telegram.Tests.EndToEnd;

/// <summary>
/// End-to-end for the T6 flow slice, from a bot author's source to a live chat: a bot that arms text
/// steps with <c>flow.PromptAsync&lt;TRequest&gt;()</c> is compiled with both the Immediate and Vexel
/// generators, loaded, registered through its generated <c>Add{Assembly}Telegram()</c>, and run in a
/// real host against a fake Telegram Bot API. A user then holds a multi-turn conversation with it and
/// the assertions are on what Telegram saw the bot send back.
/// </summary>
public sealed class FlowEndToEndTests(ITestOutputHelper output)
{
	private const string Token = "424242:AAHkQqL2b0zGf4nZ0K2QYQ0F0uSj1sM6Q8w";

	/// <summary>A two-step signup conversation, exactly as a bot author would write it.</summary>
	private const string SignupBotSource = """
		using System.Threading;
		using System.Threading.Tasks;
		using Immediate.Handlers.Shared;
		using Microsoft.Extensions.DependencyInjection;
		using Vexel.Telegram.Handlers;
		using Vexel.Telegram.Handlers.Attributes;

		namespace SignupBot;

		/// <summary>The bot's composition root: Immediate handlers + the generated Vexel routes.</summary>
		public static class BotRegistration
		{
			public static IServiceCollection AddSignupBot(IServiceCollection services) =>
				services
					.AddSignupBotHandlers()
					.AddSignupBotTelegram();
		}

		/// <summary>Draft carried between steps. JSON-serializable POCO only.</summary>
		public sealed class SignupDraft
		{
			public string Name { get; set; } = "";

			public int Age { get; set; }
		}

		[Handler]
		[Command("signup", Description = "Register with the bot")]
		public static partial class StartSignup
		{
			public sealed record Command;

			private static async ValueTask HandleAsync(
				Command command,
				Flow flow,
				Feedback feedback,
				CancellationToken token)
			{
				// Arm the next text step by its request type; CollectName itself is static.
				await flow.PromptAsync<CollectName.Command>(cancellationToken: token);
				_ = await feedback.ReplyAsync("What's your name?", cancellationToken: token);
			}
		}

		[Handler]
		public static partial class CollectName
		{
			public sealed record Command(string Name);

			private static async ValueTask HandleAsync(
				Command command,
				Flow flow,
				Feedback feedback,
				CancellationToken token)
			{
				await flow.SetDraftAsync(new SignupDraft { Name = command.Name }, token);
				await flow.PromptAsync<CollectAge.Command>(cancellationToken: token);
				_ = await feedback.ReplyAsync(
					$"Nice to meet you, {command.Name}. How old are you?",
					cancellationToken: token);
			}
		}

		[Handler]
		public static partial class CollectAge
		{
			public sealed record Command(string Age);

			private static async ValueTask HandleAsync(
				Command command,
				Flow flow,
				Feedback feedback,
				CancellationToken token)
			{
				if (!int.TryParse(command.Age, out var age))
				{
					// B3: a throwing step keeps the step armed so the user can just answer again.
					throw new System.FormatException("age was not a number");
				}

				var draft = await flow.GetDraftAsync<SignupDraft>(token) ?? new SignupDraft();
				draft.Age = age;

				// No re-arm and no explicit complete: the router auto-completes the flow.
				_ = await feedback.ReplyAsync(
					$"Registered {draft.Name}, age {age}.",
					cancellationToken: token);
			}
		}
		""";

	/// <summary>A bot that owns <c>/cancel</c> itself, so the built-in must stay out of the way.</summary>
	private const string CustomCancelBotSource = """
		using System.Threading;
		using System.Threading.Tasks;
		using Immediate.Handlers.Shared;
		using Microsoft.Extensions.DependencyInjection;
		using Vexel.Telegram.Handlers;
		using Vexel.Telegram.Handlers.Attributes;

		namespace CustomCancelBot;

		public static class BotRegistration
		{
			public static IServiceCollection AddCustomCancelBot(IServiceCollection services) =>
				services
					.AddCustomCancelBotHandlers()
					.AddCustomCancelBotTelegram();
		}

		[Handler]
		[Command("note", Description = "Start a note")]
		public static partial class StartNote
		{
			public sealed record Command;

			private static async ValueTask HandleAsync(
				Command command,
				Flow flow,
				Feedback feedback,
				CancellationToken token)
			{
				await flow.PromptAsync<CollectNote.Command>(cancellationToken: token);
				_ = await feedback.ReplyAsync("Type your note.", cancellationToken: token);
			}
		}

		[Handler]
		[Command("cancel", Description = "The app's own cancel")]
		public static partial class AppCancel
		{
			public sealed record Command;

			private static async ValueTask HandleAsync(
				Command command,
				Feedback feedback,
				CancellationToken token)
			{
				// Deliberately does NOT clear the flow, so the built-in cannot hide behind it.
				_ = await feedback.ReplyAsync(
					"App cancel here - your note is still open.",
					cancellationToken: token);
			}
		}

		[Handler]
		public static partial class CollectNote
		{
			public sealed record Command(string Text);

			private static async ValueTask HandleAsync(
				Command command,
				Feedback feedback,
				CancellationToken token)
			{
				_ = await feedback.ReplyAsync($"Saved note: {command.Text}", cancellationToken: token);
			}
		}
		""";

	[Fact]
	public async Task Bot_holds_a_multi_turn_flow_conversation_through_the_generated_step_map()
	{
		var transcript = new Transcript();
		var clock = new AdvanceableTimeProvider(
			DateTimeOffset.Parse("2026-01-01T09:00:00Z", CultureInfo.InvariantCulture));

		var botAssembly = GeneratorTestHelper.EmitBotAssembly(SignupBotSource, "SignupBot", out var generatedRoutes);
		transcript.Write("compiled SignupBot with Immediate + Vexel generators; loaded generated route table");

		await using var api = new FakeTelegramBotApi(transcript.Write);
		api.Start();
		transcript.Write($"fake Telegram Bot API listening on {api.BaseAddress} (bot username @vexel_bot)");

		using var host = BuildHost(api.BaseAddress, transcript, botAssembly, clock, "AddSignupBot");
		await host.StartAsync();
		await WaitForAsync(() => api.GetUpdatesCalls >= 2);

		var store = host.Services.GetRequiredService<IFlowStore>();

		Step[] conversation =
		[
			new(901, "/signup", IsCommand: true, ExpectedReply: "What's your name?",
				Note: "command arms the first step"),
			new(902, "Ada Lovelace", IsCommand: false,
				ExpectedReply: "Nice to meet you, Ada Lovelace. How old are you?",
				Note: "plain text goes to the armed step, which saves a draft and re-arms"),
			new(903, "thirty six", IsCommand: false,
				ExpectedReply: "Something went wrong - try again, or /cancel.",
				Note: "step throws: generic reply, step stays armed"),
			new(904, "36", IsCommand: false, ExpectedReply: "Registered Ada Lovelace, age 36.",
				Note: "retry succeeds; draft survived the throw; no re-arm auto-completes the flow"),
			new(905, "are you still there?", IsCommand: false, ExpectedReply: null,
				Note: "flow is over, so plain text is unrouted and the bot stays silent"),
			new(906, "/signup", IsCommand: true, ExpectedReply: "What's your name?",
				Note: "arm again to test interruptions"),
			new(907, "/whoami", IsCommand: true, ExpectedReply: null,
				Note: "unknown command mid-flow: command miss, and it does NOT get eaten as step input"),
			new(908, "Grace Hopper", IsCommand: false,
				ExpectedReply: "Nice to meet you, Grace Hopper. How old are you?",
				Note: "the step was still armed after the unknown command"),
			new(909, "/cancel", IsCommand: true, ExpectedReply: "Cancelled.",
				Note: "built-in /cancel wins over the armed step and clears it"),
			new(910, "42", IsCommand: false, ExpectedReply: null,
				Note: "nothing armed after cancel, so the answer is unrouted"),
			new(911, "/signup", IsCommand: true, ExpectedReply: "What's your name?",
				Note: "arm once more, then walk away"),
			new(912, "Alan Turing", IsCommand: false, ExpectedReply: null, Advance: TimeSpan.FromMinutes(16),
				Note: "16 minutes later the 15m TTL has expired, so the late answer is unrouted"),
		];

		var chat = new List<ChatLine>();

		foreach (var step in conversation)
		{
			if (step.Advance is { } advance)
			{
				clock.Advance(advance);
				transcript.Write(string.Create(
					CultureInfo.InvariantCulture,
					$"clock advances {advance.TotalMinutes:F0} minutes to {clock.GetUtcNow():u}"));
				chat.Add(new ChatLine("clock", string.Create(
					CultureInfo.InvariantCulture,
					$"... {advance.TotalMinutes:F0} minutes pass ...")));
			}

			transcript.Write($"user sends \"{step.Text}\"");
			chat.Add(new ChatLine("user", step.Text));

			var before = api.OutboundCalls.Count;
			api.Enqueue(step.IsCommand
				? FakeTelegramBotApi.CommandUpdate(step.Id, ChatId, step.Text)
				: FakeTelegramBotApi.MessageUpdate(step.Id, ChatId, step.Text));

			if (step.ExpectedReply is { } expected)
			{
				await WaitForAsync(() => api.OutboundCalls.Count > before);
				var reply = api.OutboundCalls[^1];
				Assert.Contains(
					$"text=\"{expected}\"",
					reply,
					StringComparison.Ordinal);
				chat.Add(new ChatLine("bot", expected));
			}
			else
			{
				// Silence has to be waited out: wait until Telegram actually handed the update over,
				// then give a handler the time a reply would have taken.
				await WaitForAsync(() => Delivered(api, step.Id));
				await Task.Delay(400);
				Assert.Equal(before, api.OutboundCalls.Count);
				chat.Add(new ChatLine("bot", "(silence)"));
			}
		}

		// Nothing is left armed for this user once the conversation ends.
		Assert.Null(await store.GetAsync(ChatId, ChatId));

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

		_ = EvidenceWriter.TryWrite(
			"flow-conversation-e2e.md",
			BuildEvidence("Multi-turn conversation flow, end to end", generatedRoutes, conversation, chat, transcript, calls));

		// Exactly the eight replies the conversation calls for - no accidental extra chatter.
		Assert.Equal(8, calls.Count(static c => c.StartsWith("sendMessage", StringComparison.Ordinal)));
		Assert.True(transcript.Contains("Flow step 'SignupBot.CollectAge+Command' failed"));
	}

	[Fact]
	public async Task App_owned_cancel_command_wins_over_the_built_in_cancel()
	{
		var transcript = new Transcript();
		var clock = new AdvanceableTimeProvider(
			DateTimeOffset.Parse("2026-01-01T09:00:00Z", CultureInfo.InvariantCulture));

		var botAssembly = GeneratorTestHelper.EmitBotAssembly(
			CustomCancelBotSource,
			"CustomCancelBot",
			out var generatedRoutes);

		await using var api = new FakeTelegramBotApi(transcript.Write);
		api.Start();

		using var host = BuildHost(api.BaseAddress, transcript, botAssembly, clock, "AddCustomCancelBot");
		await host.StartAsync();
		await WaitForAsync(() => api.GetUpdatesCalls >= 2);

		Step[] conversation =
		[
			new(921, "/note", IsCommand: true, ExpectedReply: "Type your note.",
				Note: "arms the note step"),
			new(922, "/cancel", IsCommand: true, ExpectedReply: "App cancel here - your note is still open.",
				Note: "the app's own [Command(\"cancel\")] runs; the built-in never fires"),
			new(923, "buy milk", IsCommand: false, ExpectedReply: "Saved note: buy milk",
				Note: "the step is still armed, because the app's cancel chose not to clear it"),
		];

		var chat = new List<ChatLine>();

		foreach (var step in conversation)
		{
			transcript.Write($"user sends \"{step.Text}\"");
			chat.Add(new ChatLine("user", step.Text));

			var before = api.OutboundCalls.Count;
			api.Enqueue(step.IsCommand
				? FakeTelegramBotApi.CommandUpdate(step.Id, ChatId, step.Text)
				: FakeTelegramBotApi.MessageUpdate(step.Id, ChatId, step.Text));

			await WaitForAsync(() => api.OutboundCalls.Count > before);
			Assert.Contains(
				$"text=\"{step.ExpectedReply}\"",
				api.OutboundCalls[^1],
				StringComparison.Ordinal);
			chat.Add(new ChatLine("bot", step.ExpectedReply!));
		}

		await host.StopAsync();

		var calls = api.OutboundCalls;
		WriteChat(output, chat);

		_ = EvidenceWriter.TryWrite(
			"flow-app-cancel-e2e.md",
			BuildEvidence("An app that owns /cancel, end to end", generatedRoutes, conversation, chat, transcript, calls));

		// The built-in confirmation text never reaches the chat.
		Assert.DoesNotContain(calls, static c => c.Contains("Cancelled.", StringComparison.Ordinal));
	}

	/// <summary>Every way a bot author can point <c>PromptAsync</c> at something that is not a step.</summary>
	private const string BadPromptBotSource = """
		using System.Threading;
		using System.Threading.Tasks;
		using Immediate.Handlers.Shared;
		using Vexel.Telegram.Handlers;
		using Vexel.Telegram.Handlers.Attributes;

		namespace SignupBot;

		public sealed record NotAHandlerRequest;

		[Handler]
		[Command("ping")]
		public static class Ping
		{
			public sealed record Command(string Text);

			private static ValueTask HandleAsync(Command request, CancellationToken token) => default;
		}

		[Handler]
		public static class CollectCount
		{
			public sealed record Command(int Count);

			private static ValueTask HandleAsync(Command request, CancellationToken token) => default;
		}

		[Handler]
		public static class CollectName
		{
			public sealed record Command(string Name);

			private static ValueTask HandleAsync(Command request, CancellationToken token) => default;
		}

		public static class Prompts
		{
			public static ValueTask NotAHandler(Flow flow, CancellationToken token) =>
				flow.PromptAsync<NotAHandlerRequest>(cancellationToken: token);

			public static ValueTask RoutedHandler(Flow flow, CancellationToken token) =>
				flow.PromptAsync<Ping.Command>(cancellationToken: token);

			public static ValueTask UnbindableRequest(Flow flow, CancellationToken token) =>
				flow.PromptAsync<CollectCount.Command>(cancellationToken: token);

			public static ValueTask ValidStep(Flow flow, CancellationToken token) =>
				flow.PromptAsync<CollectName.Command>(cancellationToken: token);
		}
		""";

	[Fact]
	public async Task Prompting_a_non_step_target_breaks_the_build_with_VEX0006()
	{
		var diagnostics = await GeneratorTestHelper.RunAnalyzersAsync(
			BadPromptBotSource,
			new Vexel.Telegram.Generators.Analyzers.InvalidPromptTargetAnalyzer());

		var reported = diagnostics
			.OrderBy(static d => d.Location.GetLineSpan().StartLinePosition.Line)
			.Select(static d => string.Create(
				CultureInfo.InvariantCulture,
				$"Bot.cs({d.Location.GetLineSpan().StartLinePosition.Line + 1},"
				+ $"{d.Location.GetLineSpan().StartLinePosition.Character + 1}): "
				+ $"{d.Severity.ToString().ToLowerInvariant()} {d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}"))
			.ToArray();

		output.WriteLine("Build output the bot author sees:");
		foreach (var line in reported)
		{
			output.WriteLine($"  {line}");
		}

		var evidence = new StringBuilder();
		evidence.AppendLine("# VEX0006: a bad `PromptAsync` target fails the build");
		evidence.AppendLine();
		evidence.AppendLine("The bot author writes this and hits Build:");
		evidence.AppendLine();
		evidence.AppendLine("```csharp");
		evidence.AppendLine(BadPromptBotSource);
		evidence.AppendLine("```");
		evidence.AppendLine();
		evidence.AppendLine("Compiler output (error severity, so the build stops):");
		evidence.AppendLine();
		evidence.AppendLine("```text");
		foreach (var line in reported)
		{
			evidence.AppendLine(line);
		}

		evidence.AppendLine("```");
		evidence.AppendLine();
		evidence.AppendLine(
			"`ValidStep`, which prompts the request of a routeless `[Handler]` with a single-string "
			+ "request, produces no diagnostic.");
		evidence.AppendLine();

		_ = EvidenceWriter.TryWrite("flow-vex0006-build-errors.md", evidence.ToString());

		// The three bad prompts each fail the build; the valid one is left alone.
		Assert.Equal(3, reported.Length);
		Assert.All(diagnostics, static d => Assert.Equal("VEX0006", d.Id));
		Assert.All(diagnostics, static d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
		Assert.DoesNotContain(reported, static r => r.Contains("CollectName", StringComparison.Ordinal));
	}

	private const long ChatId = 100;

	/// <summary>True once the fake server has handed update <paramref name="id"/> to the bot.</summary>
	private static bool Delivered(FakeTelegramBotApi api, int id) =>
		api.Requests.Any(r => r.Contains($",{id}]", StringComparison.Ordinal)
			|| r.EndsWith($"[{id}]", StringComparison.Ordinal));

	private static void WriteChat(ITestOutputHelper output, IEnumerable<ChatLine> chat)
	{
		output.WriteLine("Chat as the user sees it:");
		foreach (var line in chat)
		{
			output.WriteLine($"  {Render(line)}");
		}
	}

	private static string Render(ChatLine line) => line.Speaker switch
	{
		"user" => $"user  >  {line.Text}",
		"bot" => $"  bot <  {line.Text}",
		_ => $"         {line.Text}",
	};

	private static string BuildEvidence(
		string title,
		string generatedRoutes,
		IReadOnlyList<Step> conversation,
		IReadOnlyList<ChatLine> chat,
		Transcript transcript,
		IReadOnlyList<string> outboundCalls)
	{
		var writer = new StringBuilder();

		writer.AppendLine(CultureInfo.InvariantCulture, $"# {title}");
		writer.AppendLine();
		writer.AppendLine(
			"A bot that arms text steps with `flow.PromptAsync<TRequest>()` is compiled with the");
		writer.AppendLine(
			"Immediate and Vexel generators, loaded, registered through its generated");
		writer.AppendLine(
			"`Add{Assembly}Telegram()`, and run in a real host against a fake Telegram Bot API server");
		writer.AppendLine("over HTTP. Every line below is a real message on the wire.");
		writer.AppendLine();

		writer.AppendLine("## 1. The chat, as the user sees it");
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
		writer.AppendLine("| User sends | Bot replies | Behaviour |");
		writer.AppendLine("| --- | --- | --- |");
		foreach (var step in conversation)
		{
			writer.AppendLine(CultureInfo.InvariantCulture,
				$"| `{step.Text}` | {(step.ExpectedReply is null ? "_(silence)_" : $"`{step.ExpectedReply}`")} | {step.Note} |");
		}

		writer.AppendLine();

		writer.AppendLine("## 2. Flow step map the Vexel generator emitted (`Vexel.Telegram.Routes.g.cs`)");
		writer.AppendLine();
		writer.AppendLine("```csharp");
		writer.AppendLine(generatedRoutes.TrimEnd());
		writer.AppendLine("```");
		writer.AppendLine();

		writer.AppendLine("## 3. Live session log");
		writer.AppendLine();
		writer.AppendLine("```text");
		foreach (var line in transcript.Lines)
		{
			writer.AppendLine(line);
		}

		writer.AppendLine("```");
		writer.AppendLine();

		writer.AppendLine("### Bot -> Telegram Bot API calls, in order");
		writer.AppendLine();
		writer.AppendLine("```text");
		foreach (var call in outboundCalls)
		{
			writer.AppendLine(call);
		}

		writer.AppendLine("```");
		writer.AppendLine();

		return writer.ToString();
	}

	private static IHost BuildHost(
		string apiAddress,
		Transcript transcript,
		Assembly botAssembly,
		TimeProvider clock,
		string registrationMethod)
	{
		var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

		_ = builder.Logging.SetMinimumLevel(LogLevel.Information);
		_ = builder.Services.AddSingleton<ILoggerProvider>(_ => new TranscriptLoggerProvider(transcript));

		_ = builder.Services.AddSingleton<ITelegramBotClient>(
			_ => new TelegramBotClient(new TelegramBotClientOptions(Token, apiAddress)));

		// Registered before AddTelegramBot so the flow store and Flow take this clock (TryAddSingleton).
		_ = builder.Services.AddSingleton(clock);

		_ = builder.Services.AddTelegramBot(_ => Token);
		Invoke(botAssembly, registrationMethod, builder.Services);

		return builder.Build();
	}

	/// <summary>Calls a generated <c>IServiceCollection</c> extension method by name.</summary>
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

	/// <summary>One turn of the scripted conversation.</summary>
	private sealed record Step(
		int Id,
		string Text,
		bool IsCommand,
		string? ExpectedReply,
		string Note,
		TimeSpan? Advance = null);

	private sealed record ChatLine(string Speaker, string Text);

	/// <summary>Clock the test can move forward to reach a flow TTL without waiting 15 real minutes.</summary>
	private sealed class AdvanceableTimeProvider(DateTimeOffset start) : TimeProvider
	{
		private long _ticks = start.UtcTicks;

		public override DateTimeOffset GetUtcNow() => new(Volatile.Read(ref _ticks), TimeSpan.Zero);

		public void Advance(TimeSpan delta) => Volatile.Write(ref _ticks, Volatile.Read(ref _ticks) + delta.Ticks);
	}
}
