using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Vexel.Telegram.Handlers.DependencyInjection;
using Vexel.Telegram.Tests.Generators;
using Xunit.Abstractions;

namespace Vexel.Telegram.Tests.EndToEnd;

/// <summary>
/// End-to-end for the T5 slice, from a bot author's source to a live chat: a bot assembly written
/// with <c>[Handler]</c> + <c>[Callback]</c> and keyboards built by
/// <c>Keyboards.InlineKeyboardBuilder</c> is compiled with both the Immediate and Vexel generators,
/// loaded, registered through its generated <c>Add{Assembly}Telegram()</c>, and run in a real host
/// against a fake Telegram Bot API. Users then tap buttons and the assertions are on what Telegram
/// saw the bot send back - including the fail-closed <c>answerCallbackQuery</c> obligation.
/// </summary>
public sealed class CallbackRoutingEndToEndTests(ITestOutputHelper output)
{
	private const string Token = "424242:AAHkQqL2b0zGf4nZ0K2QYQ0F0uSj1sM6Q8w";

	/// <summary>The bot author's own project, exactly as they would write it.</summary>
	private const string BotSource = """
		using System;
		using System.Threading;
		using System.Threading.Tasks;
		using Immediate.Handlers.Shared;
		using Microsoft.Extensions.DependencyInjection;
		using Vexel.Telegram.Handlers;
		using Vexel.Telegram.Handlers.Attributes;
		using Vexel.Telegram.Handlers.Keyboards;

		namespace DemoBot;

		/// <summary>The bot's composition root: Immediate handlers + the generated Vexel routes.</summary>
		public static class BotRegistration
		{
			public static IServiceCollection AddDemoBot(IServiceCollection services) =>
				services
					.AddDemoBotHandlers()
					.AddDemoBotTelegram();
		}

		/// <summary>Sends the inline keyboard whose buttons carry the callback routes.</summary>
		[Handler]
		[Command("menu", Description = "Show the order menu")]
		public static partial class Menu
		{
			public sealed record Command;

			private static async ValueTask HandleAsync(
				Command command,
				Feedback feedback,
				CancellationToken token)
			{
				var keyboard = new InlineKeyboardBuilder()
					.AddRow()
					.AddCallbackButton("Confirm", "confirm", "order-42")
					.AddCallbackButton("Cancel", "cancel")
					.AddRow()
					.AddUrlButton("Docs", "https://example.com/orders")
					.Build();

				_ = await feedback.SendWithKeyboardAsync("Order 42: what next?", keyboard, cancellationToken: token);
			}
		}

		/// <summary>Suffix after the first '|' binds to the single string request parameter.</summary>
		[Handler]
		[Callback("confirm")]
		public static partial class Confirm
		{
			public sealed record Command(string OrderId);

			private static async ValueTask HandleAsync(
				Command command,
				Feedback feedback,
				CancellationToken token)
			{
				await feedback.AnswerCallbackAsync($"Confirmed {command.OrderId}", cancellationToken: token);
				await feedback.EditAsync($"{command.OrderId} confirmed.", cancellationToken: token);
			}
		}

		/// <summary>Empty record: ignores any suffix, and never answers the query itself.</summary>
		[Handler]
		[Callback("cancel")]
		public static partial class Cancel
		{
			public sealed record Command;

			private static async ValueTask HandleAsync(
				Command command,
				Feedback feedback,
				CancellationToken token)
			{
				await feedback.EditAsync("Order cancelled.", cancellationToken: token);
			}
		}

		/// <summary>A handler that faults after the tap but before answering.</summary>
		[Handler]
		[Callback("boom")]
		public static partial class Boom
		{
			public sealed record Command;

			private static ValueTask HandleAsync(Command command, CancellationToken token) =>
				throw new InvalidOperationException("handler blew up");
		}
		""";

	[Fact]
	public async Task Bot_RoutesButtonTaps_AndAlwaysAnswersTheCallbackQuery()
	{
		var transcript = new Transcript();

		// Compile the bot author's project with both generators and load the result, so the callback
		// routes under test are the generator's own emitted code, not a hand-written stand-in.
		var botAssembly = GeneratorTestHelper.EmitBotAssembly(BotSource, "DemoBot", out var generatedRoutes);
		transcript.Write("compiled DemoBot with Immediate + Vexel generators; loaded generated route table");

		await using var api = new FakeTelegramBotApi(transcript.Write);
		api.Start();
		transcript.Write($"fake Telegram Bot API listening on {api.BaseAddress} (bot username @vexel_bot)");

		using var host = BuildHost(api.BaseAddress, transcript, botAssembly);
		await host.StartAsync();
		await WaitForAsync(() => api.GetUpdatesCalls >= 2);

		// 1. The user opens the menu, so the bot sends the inline keyboard the helpers built.
		transcript.Write("user in chat 100 sends \"/menu\"");
		api.Enqueue(FakeTelegramBotApi.CommandUpdate(950, 100, "/menu"));
		await WaitForAsync(() => api.OutboundCalls.Any(
			static c => c.StartsWith("sendMessage", StringComparison.Ordinal)));

		var keyboardCall = api.OutboundCalls.Single(
			static c => c.StartsWith("sendMessage", StringComparison.Ordinal));
		var botMessageId = 9001;

		// 2. Every tap the user makes on (or around) that keyboard.
		var taps = new (int UpdateId, string CallbackId, string Data, string What)[]
		{
			(951, "cb-confirm", "confirm|order-42", "taps [Confirm] (callback_data \"confirm|order-42\")"),
			(952, "cb-cancel", "cancel", "taps [Cancel] (callback_data \"cancel\")"),
			(953, "cb-boom", "boom", "taps a button whose handler throws (callback_data \"boom\")"),
			(954, "cb-ghost", "ghost|1", "taps a stale button no handler claims (callback_data \"ghost|1\")"),
		};

		foreach (var (updateId, callbackId, data, what) in taps)
		{
			transcript.Write(string.Create(CultureInfo.InvariantCulture, $"user in chat 100 {what}"));

			api.Enqueue(FakeTelegramBotApi.CallbackQueryUpdate(updateId, 100, botMessageId, callbackId, data));

			// Telegram considers the tap handled once the bot answers the query.
			await WaitForAsync(() => api.OutboundCalls.Any(
				c => c.Contains($"callback_query_id=\"{callbackId}\"", StringComparison.Ordinal)));
		}

		await host.StopAsync();
		transcript.Write("host stopped");

		var calls = api.OutboundCalls;

		foreach (var line in transcript.Lines)
		{
			output.WriteLine(line);
		}

		output.WriteLine(string.Empty);
		output.WriteLine("Bot -> Telegram Bot API calls, as the Telegram side saw them:");
		foreach (var call in calls)
		{
			output.WriteLine($"  {call}");
		}

		_ = EvidenceWriter.TryWrite(
			"callback-routing-e2e.md",
			BuildEvidence(generatedRoutes, transcript, calls));

		// The keyboard helper emitted short `key` / `key|suffix` callback data on the buttons.
		Assert.Contains("callback_data=\"confirm|order-42\"", keyboardCall, StringComparison.Ordinal);
		Assert.Contains("callback_data=\"cancel\"", keyboardCall, StringComparison.Ordinal);
		Assert.Contains("url=\"https://example.com/orders\"", keyboardCall, StringComparison.Ordinal);

		var answers = calls
			.Where(static c => c.StartsWith("answerCallbackQuery", StringComparison.Ordinal))
			.ToArray();
		var edits = calls
			.Where(static c => c.StartsWith("editMessageText", StringComparison.Ordinal))
			.ToArray();

		// Suffix bound to the handler's string parameter; the app's own answer text is what the user
		// sees, and the obligation does not answer a second time.
		Assert.Contains(
			answers,
			static a => a.Contains("callback_query_id=\"cb-confirm\"", StringComparison.Ordinal)
				&& a.Contains("text=\"Confirmed order-42\"", StringComparison.Ordinal));
		Assert.Contains(edits, static e => e.Contains("text=\"order-42 confirmed.\"", StringComparison.Ordinal));

		// Empty-record route ran (message edited) and the obligation still answered for it.
		Assert.Contains(edits, static e => e.Contains("text=\"Order cancelled.\"", StringComparison.Ordinal));
		Assert.Contains(
			answers,
			static a => a.Contains("callback_query_id=\"cb-cancel\"", StringComparison.Ordinal)
				&& !a.Contains("text=", StringComparison.Ordinal));

		// Fail-closed: a throwing handler and an unrouted tap are still answered, so the client
		// spinner never hangs, and neither leaks anything into the chat.
		Assert.Contains(
			answers,
			static a => a.Contains("callback_query_id=\"cb-boom\"", StringComparison.Ordinal)
				&& !a.Contains("text=", StringComparison.Ordinal));
		Assert.Contains(
			answers,
			static a => a.Contains("callback_query_id=\"cb-ghost\"", StringComparison.Ordinal)
				&& !a.Contains("text=", StringComparison.Ordinal));

		// Exactly one answer per tap: no duplicates from the obligation, none missing.
		Assert.Equal(taps.Length, answers.Length);
		Assert.Equal(2, edits.Length);
		Assert.True(transcript.Contains("handler blew up"));
	}

	private static string BuildEvidence(
		string generatedRoutes,
		Transcript transcript,
		IReadOnlyList<string> outboundCalls)
	{
		var writer = new StringBuilder();

		writer.AppendLine("# `[Callback]` routing and the answer obligation, end to end");
		writer.AppendLine();
		writer.AppendLine(
			"A bot assembly written with `[Handler]` + `[Callback]` and keyboards built by");
		writer.AppendLine(
			"`InlineKeyboardBuilder` is compiled with the Immediate and Vexel generators, loaded,");
		writer.AppendLine(
			"registered through its generated `AddDemoBotTelegram()`, and run in a real host against a");
		writer.AppendLine("fake Telegram Bot API server over HTTP.");
		writer.AppendLine();

		writer.AppendLine("## 1. What the bot author wrote");
		writer.AppendLine();
		writer.AppendLine("```csharp");
		writer.AppendLine(BotSource);
		writer.AppendLine("```");
		writer.AppendLine();

		writer.AppendLine("## 2. Route table the Vexel generator emitted (`Vexel.Telegram.Routes.g.cs`)");
		writer.AppendLine();
		writer.AppendLine("```csharp");
		writer.AppendLine(generatedRoutes.TrimEnd());
		writer.AppendLine("```");
		writer.AppendLine();

		writer.AppendLine("## 3. Live session: button taps and what Telegram saw the bot send");
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

		writer.AppendLine("### What each tap produced");
		writer.AppendLine();
		writer.AppendLine("| Tap | Route | Chat effect | Answer |");
		writer.AppendLine("| --- | --- | --- | --- |");
		writer.AppendLine(
			"| `confirm\\|order-42` | `[Callback(\"confirm\")]`, suffix bound to `OrderId` | message edited to `order-42 confirmed.` | app answer `Confirmed order-42` (obligation stays quiet) |");
		writer.AppendLine(
			"| `cancel` | `[Callback(\"cancel\")]`, empty record | message edited to `Order cancelled.` | default empty answer from the obligation |");
		writer.AppendLine(
			"| `boom` | `[Callback(\"boom\")]`, handler throws | nothing | default empty answer from the obligation |");
		writer.AppendLine(
			"| `ghost\\|1` | no route matches | nothing | default empty answer from the obligation |");
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

		// Vexel host wiring plus the bot's own composition root
		// (AddDemoBotHandlers() + generated AddDemoBotTelegram()).
		_ = builder.Services.AddTelegramBot(_ => Token);
		Invoke(botAssembly, "AddDemoBot", builder.Services);

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
}
