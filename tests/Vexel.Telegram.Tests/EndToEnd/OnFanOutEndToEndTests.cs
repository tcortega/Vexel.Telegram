using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Vexel.Telegram.Client;
using Vexel.Telegram.Client.Extensions;
using Vexel.Telegram.Handlers.DependencyInjection;
using Vexel.Telegram.Tests.Generators;
using Xunit.Abstractions;

namespace Vexel.Telegram.Tests.EndToEnd;

/// <summary>
/// End-to-end for the T8 slice, from a bot author's source to a live chat: two independently
/// compiled bot assemblies written with <c>[Handler]</c> + route attributes + <c>[On*]</c> are
/// compiled with both the Immediate and Vexel generators, loaded, registered through their
/// generated <c>Add{Assembly}Telegram()</c>, and run in a real host against a fake Telegram Bot
/// API. The assertions are on the order Telegram saw the bot's calls arrive: routed handler first,
/// then every On* observer in fully-qualified metadata name order (across assembly boundaries),
/// then the raw handler, then the fail-closed answer obligation.
/// </summary>
public sealed class OnFanOutEndToEndTests(ITestOutputHelper output)
{
	private const string Token = "424242:AAHkQqL2b0zGf4nZ0K2QYQ0F0uSj1sM6Q8w";

	/// <summary>
	/// The app's own project: routed handlers for all four kinds plus two message observers whose
	/// names bracket the observer the plugin library contributes.
	/// </summary>
	private const string AppSource = """
		using System.Threading;
		using System.Threading.Tasks;
		using Immediate.Handlers.Shared;
		using Microsoft.Extensions.DependencyInjection;
		using Telegram.Bot;
		using Telegram.Bot.Types.InlineQueryResults;
		using Vexel.Telegram.Handlers;
		using Vexel.Telegram.Handlers.Attributes;
		using Vexel.Telegram.Handlers.Contexts;

		namespace DemoBot
		{
			/// <summary>The app's composition root: Immediate handlers + the generated Vexel routes.</summary>
			public static class BotRegistration
			{
				public static IServiceCollection AddDemoBot(IServiceCollection services) =>
					services
						.AddDemoBotHandlers()
						.AddDemoBotTelegram();
			}
		}

		namespace DemoBot.Routes
		{
			[Handler]
			[Command("ping", Description = "Ping the bot")]
			public static partial class Ping
			{
				public sealed record Command;

				private static async ValueTask HandleAsync(
					Command command,
					Feedback feedback,
					CancellationToken token)
				{
					_ = await feedback.ReplyAsync("routed: /ping -> pong", cancellationToken: token);
				}
			}

			/// <summary>Answers nothing, so the B4 obligation has to close the callback out.</summary>
			[Handler]
			[Callback("confirm")]
			public static partial class Confirm
			{
				public sealed record Command;

				private static async ValueTask HandleAsync(
					Command command,
					Feedback feedback,
					CancellationToken token)
				{
					_ = await feedback.ReplyAsync("routed: callback confirm", cancellationToken: token);
				}
			}

			[Handler]
			[InlineQuery("search")]
			public static partial class SearchInline
			{
				public sealed record Query(string Text);

				private static async ValueTask HandleAsync(
					Query query,
					Feedback feedback,
					CancellationToken token)
				{
					await feedback.AnswerInlineAsync(
						[
							new InlineQueryResultArticle(
								$"item|{query.Text}",
								$"routed: search {query.Text}",
								new InputTextMessageContent($"You searched for {query.Text}")),
						],
						cacheTime: 30,
						cancellationToken: token);
				}
			}

			[Handler]
			[ChosenInlineResult("item")]
			public static partial class ItemChosen
			{
				public sealed record Command(string Term);

				private static async ValueTask HandleAsync(
					Command command,
					Feedback feedback,
					CancellationToken token)
				{
					await feedback.EditAsync(
						$"routed: chosen {command.Term}",
						cancellationToken: token);
				}
			}
		}

		namespace Watchers
		{
			/// <summary>Sorts first of the three message observers.</summary>
			[Handler]
			[OnMessage]
			public static partial class AlphaAudit
			{
				public sealed record Command;

				private static async ValueTask HandleAsync(
					Command command,
					Feedback feedback,
					CancellationToken token)
				{
					_ = await feedback.ReplyAsync("on-message: Watchers.AlphaAudit", cancellationToken: token);
				}
			}

			/// <summary>Sorts last of the three message observers.</summary>
			[Handler]
			[OnMessage]
			public static partial class ZuluAudit
			{
				public sealed record Command;

				private static async ValueTask HandleAsync(
					Command command,
					Feedback feedback,
					CancellationToken token)
				{
					_ = await feedback.ReplyAsync("on-message: Watchers.ZuluAudit", cancellationToken: token);
				}
			}

			[Handler]
			[OnCallbackQuery]
			public static partial class CallbackAudit
			{
				public sealed record Command;

				private static async ValueTask HandleAsync(
					Command command,
					Feedback feedback,
					CancellationToken token)
				{
					_ = await feedback.ReplyAsync(
						"on-callback: Watchers.CallbackAudit",
						cancellationToken: token);
				}
			}

			/// <summary>
			/// Inline queries are chatless, so the observer reports by messaging the querier
			/// directly instead of answering (the routed handler owns the answer).
			/// </summary>
			[Handler]
			[OnInlineQuery]
			public static partial class InlineAudit
			{
				public sealed record Command;

				private static async ValueTask HandleAsync(
					Command command,
					InlineQueryContext context,
					ITelegramBotClient bot,
					CancellationToken token)
				{
					_ = await bot.SendMessage(
						context.UserId,
						"on-inline: Watchers.InlineAudit",
						cancellationToken: token);
				}
			}

			[Handler]
			[OnChosenInlineResult]
			public static partial class ChosenAudit
			{
				public sealed record Command;

				private static async ValueTask HandleAsync(
					Command command,
					ChosenInlineResultContext context,
					ITelegramBotClient bot,
					CancellationToken token)
				{
					_ = await bot.SendMessage(
						context.UserId,
						"on-chosen: Watchers.ChosenAudit",
						cancellationToken: token);
				}
			}
		}
		""";

	/// <summary>
	/// A separate handler library the app also registers. Its observer sorts between the app's two
	/// message observers, so append-per-assembly ordering would be visible immediately.
	/// </summary>
	private const string PluginSource = """
		using System.Threading;
		using System.Threading.Tasks;
		using Immediate.Handlers.Shared;
		using Microsoft.Extensions.DependencyInjection;
		using Vexel.Telegram.Handlers;
		using Vexel.Telegram.Handlers.Attributes;

		namespace PluginBot
		{
			public static class PluginRegistration
			{
				public static IServiceCollection AddPluginBot(IServiceCollection services) =>
					services
						.AddPluginBotHandlers()
						.AddPluginBotTelegram();
			}
		}

		namespace Watchers
		{
			[Handler]
			[OnMessage]
			public static partial class MikeAudit
			{
				public sealed record Command;

				private static async ValueTask HandleAsync(
					Command command,
					Feedback feedback,
					CancellationToken token)
				{
					_ = await feedback.ReplyAsync("on-message: Watchers.MikeAudit", cancellationToken: token);
				}
			}
		}
		""";

	[Fact]
	public async Task Bot_RunsOnObservers_AfterRoutedHandler_InFullyQualifiedNameOrder()
	{
		var transcript = new Transcript();

		// Compile both projects with the real generators, so the fan-out under test is the
		// generator's own emitted dispatch arrays, not a hand-written stand-in.
		var appAssembly = GeneratorTestHelper.EmitBotAssembly(AppSource, "DemoBot", out var appRoutes);
		var pluginAssembly = GeneratorTestHelper.EmitBotAssembly(PluginSource, "PluginBot", out var pluginRoutes);
		transcript.Write("compiled DemoBot + PluginBot with Immediate + Vexel generators");
		transcript.Write("registered AddDemoBotTelegram() first, AddPluginBotTelegram() second");

		await using var api = new FakeTelegramBotApi(transcript.Write);
		api.Start();
		transcript.Write($"fake Telegram Bot API listening on {api.BaseAddress} (bot username @vexel_bot)");

		using var host = BuildHost(api.BaseAddress, transcript, appAssembly, pluginAssembly);
		await host.StartAsync();
		await WaitForAsync(() => api.GetUpdatesCalls >= 2);

		// Start-up publishes the command menu and clears any leftover webhook before the first poll;
		// the fan-out assertions below count from the first update-driven call, not from those.
		var startup = api.OutboundCalls.Count;

		// 1. A routed command: routed reply, then the three message observers, then raw.
		transcript.Write("user in chat 100 sends \"/ping\"");
		api.Enqueue(FakeTelegramBotApi.CommandUpdate(901, chatId: 100, "/ping"));
		await WaitForAsync(() => OutboundSince(api, startup).Count == 5);
		var commandCalls = OutboundSince(api, startup);

		// 2. Plain text nobody routes: the observers still run.
		transcript.Write("user in chat 100 sends \"just chatting\"");
		api.Enqueue(FakeTelegramBotApi.MessageUpdate(902, chatId: 100, "just chatting"));
		await WaitForAsync(() => OutboundSince(api, startup + 5).Count == 4);
		var unroutedCalls = OutboundSince(api, startup + 5);

		// 3. A callback tap: routed, observer, raw, then the fail-closed answer obligation.
		transcript.Write("user in chat 100 taps the \"confirm\" button");
		api.Enqueue(FakeTelegramBotApi.CallbackQueryUpdate(
			903,
			chatId: 100,
			botMessageId: 55,
			callbackQueryId: "cq-903",
			data: "confirm"));
		await WaitForAsync(() => OutboundSince(api, startup + 9).Count == 4);
		var callbackCalls = OutboundSince(api, startup + 9);

		// 4. An inline query, then the result the user picks out of it.
		transcript.Write("user 300 types \"@vexel_bot search widgets\"");
		api.Enqueue(FakeTelegramBotApi.InlineQueryUpdate(904, userId: 300, "iq-904", "search widgets"));
		await WaitForAsync(() => OutboundSince(api, startup + 13).Count == 3);
		var inlineCalls = OutboundSince(api, startup + 13);

		transcript.Write("user 300 picks the \"item|widgets\" result");
		api.Enqueue(FakeTelegramBotApi.ChosenInlineResultUpdate(
			905,
			userId: 300,
			resultId: "item|widgets",
			query: "search widgets",
			inlineMessageId: "im-905"));
		await WaitForAsync(() => OutboundSince(api, startup + 16).Count == 3);
		var chosenCalls = OutboundSince(api, startup + 16);

		await host.StopAsync();
		transcript.Write("host stopped");

		foreach (var line in transcript.Lines)
		{
			output.WriteLine(line);
		}

		output.WriteLine(string.Empty);
		output.WriteLine("Bot -> Telegram Bot API calls, as the Telegram side saw them:");
		foreach (var call in api.OutboundCalls)
		{
			output.WriteLine($"  {call}");
		}

		_ = EvidenceWriter.TryWrite(
			"on-fan-out-e2e.md",
			BuildEvidence(
				appRoutes,
				pluginRoutes,
				transcript,
				commandCalls,
				unroutedCalls,
				callbackCalls,
				inlineCalls,
				chosenCalls));

		// Routed handler first, then the observers, then raw. PluginBot's Watchers.MikeAudit lands
		// between the app's own two observers: order is by FQ name, not by registration order.
		Assert.Equal(
			[
				"sendMessage chat_id=100 text=\"routed: /ping -> pong\"",
				"sendMessage chat_id=100 text=\"on-message: Watchers.AlphaAudit\"",
				"sendMessage chat_id=100 text=\"on-message: Watchers.MikeAudit\"",
				"sendMessage chat_id=100 text=\"on-message: Watchers.ZuluAudit\"",
				"sendMessage chat_id=100 text=\"raw: update 901\"",
			],
			commandCalls);

		// No route matched, so the observers are the only thing that sees the message.
		Assert.Equal(
			[
				"sendMessage chat_id=100 text=\"on-message: Watchers.AlphaAudit\"",
				"sendMessage chat_id=100 text=\"on-message: Watchers.MikeAudit\"",
				"sendMessage chat_id=100 text=\"on-message: Watchers.ZuluAudit\"",
				"sendMessage chat_id=100 text=\"raw: update 902\"",
			],
			unroutedCalls);

		// Full precedence on one update: routed -> On* -> raw -> completion hook.
		Assert.Equal(
			[
				"sendMessage chat_id=100 text=\"routed: callback confirm\"",
				"sendMessage chat_id=100 text=\"on-callback: Watchers.CallbackAudit\"",
				"sendMessage chat_id=100 text=\"raw: update 903\"",
				"answerCallbackQuery callback_query_id=\"cq-903\"",
			],
			callbackCalls);

		Assert.Equal(
			[
				"answerInlineQuery inline_query_id=\"iq-904\" cache_time=30 "
					+ "results=[item|widgets -> \"routed: search widgets\"]",
				"sendMessage chat_id=300 text=\"on-inline: Watchers.InlineAudit\"",
				"sendMessage chat_id=300 text=\"raw: update 904\"",
			],
			inlineCalls);

		Assert.Equal(
			[
				"editMessageText inline_message_id=\"im-905\" text=\"routed: chosen widgets\"",
				"sendMessage chat_id=300 text=\"on-chosen: Watchers.ChosenAudit\"",
				"sendMessage chat_id=300 text=\"raw: update 905\"",
			],
			chosenCalls);
	}

	private static List<string> OutboundSince(FakeTelegramBotApi api, int skip) =>
		[.. api.OutboundCalls.Skip(skip)];

	private static string BuildEvidence(
		string appRoutes,
		string pluginRoutes,
		Transcript transcript,
		IReadOnlyList<string> commandCalls,
		IReadOnlyList<string> unroutedCalls,
		IReadOnlyList<string> callbackCalls,
		IReadOnlyList<string> inlineCalls,
		IReadOnlyList<string> chosenCalls)
	{
		var writer = new StringBuilder();

		writer.AppendLine("# `[On*]` fan-out, end to end");
		writer.AppendLine();
		writer.AppendLine(
			"Two bot assemblies - the app (`DemoBot`) and a handler library it consumes (`PluginBot`) -");
		writer.AppendLine(
			"are compiled with the Immediate and Vexel generators, loaded, registered through their");
		writer.AppendLine(
			"generated `Add{Assembly}Telegram()` (app first, plugin second), and run in a real host");
		writer.AppendLine("against a fake Telegram Bot API server over HTTP.");
		writer.AppendLine();
		writer.AppendLine(
			"`Watchers.MikeAudit` comes from the *second* registered assembly and still runs between the");
		writer.AppendLine(
			"app's own `Watchers.AlphaAudit` and `Watchers.ZuluAudit`: dispatch order is the observers'");
		writer.AppendLine("fully-qualified metadata name, not registration order.");
		writer.AppendLine();

		writer.AppendLine("## 1. What the bot author wrote");
		writer.AppendLine();
		writer.AppendLine("### App assembly `DemoBot`");
		writer.AppendLine();
		writer.AppendLine("```csharp");
		writer.AppendLine(AppSource);
		writer.AppendLine("```");
		writer.AppendLine();
		writer.AppendLine("### Handler library `PluginBot`");
		writer.AppendLine();
		writer.AppendLine("```csharp");
		writer.AppendLine(PluginSource);
		writer.AppendLine("```");
		writer.AppendLine();

		writer.AppendLine("## 2. What Telegram saw, per update");
		writer.AppendLine();

		AppendCallBlock(
			writer,
			"### `/ping` - routed handler, then On* observers, then the raw handler",
			commandCalls);
		AppendCallBlock(
			writer,
			"### `just chatting` - nothing routes, observers still run",
			unroutedCalls);
		AppendCallBlock(
			writer,
			"### callback tap - routed -> On* -> raw -> fail-closed answer obligation",
			callbackCalls);
		AppendCallBlock(writer, "### inline query `search widgets`", inlineCalls);
		AppendCallBlock(writer, "### chosen inline result `item|widgets`", chosenCalls);

		writer.AppendLine("## 3. Live session transcript");
		writer.AppendLine();
		writer.AppendLine("```text");
		foreach (var line in transcript.Lines)
		{
			writer.AppendLine(line);
		}

		writer.AppendLine("```");
		writer.AppendLine();

		writer.AppendLine("## 4. Dispatch arrays the Vexel generator emitted");
		writer.AppendLine();
		writer.AppendLine("### `DemoBot` (`Vexel.Telegram.Routes.g.cs`)");
		writer.AppendLine();
		writer.AppendLine("```csharp");
		writer.AppendLine(appRoutes.TrimEnd());
		writer.AppendLine("```");
		writer.AppendLine();
		writer.AppendLine("### `PluginBot` (`Vexel.Telegram.Routes.g.cs`)");
		writer.AppendLine();
		writer.AppendLine("```csharp");
		writer.AppendLine(pluginRoutes.TrimEnd());
		writer.AppendLine("```");
		writer.AppendLine();

		return writer.ToString();
	}

	private static void AppendCallBlock(StringBuilder writer, string heading, IReadOnlyList<string> calls)
	{
		writer.AppendLine(heading);
		writer.AppendLine();
		writer.AppendLine("```text");
		foreach (var call in calls)
		{
			writer.AppendLine(call);
		}

		writer.AppendLine("```");
		writer.AppendLine();
	}

	private static IHost BuildHost(
		string apiAddress,
		Transcript transcript,
		Assembly appAssembly,
		Assembly pluginAssembly)
	{
		var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

		_ = builder.Logging.SetMinimumLevel(LogLevel.Information);
		_ = builder.Services.AddSingleton<ILoggerProvider>(_ => new TranscriptLoggerProvider(transcript));

		_ = builder.Services.AddSingleton<ITelegramBotClient>(
			_ => new TelegramBotClient(new TelegramBotClientOptions(Token, apiAddress)));

		_ = builder.Services.AddTelegramBot(_ => Token);

		// App composition root first, consumed handler library second: FQ-name ordering must not
		// depend on which one registered first.
		Invoke(appAssembly, "AddDemoBot", builder.Services);
		Invoke(pluginAssembly, "AddPluginBot", builder.Services);

		// Raw handlers run after routed + On*, so they mark the end of each update's fan-out.
		_ = builder.Services.AddRawUpdateHandler<MarkerRawHandler>();

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

	/// <summary>
	/// A raw handler registered the ordinary way; it sends one marker call per update so the raw
	/// stage is visible in the same ordered call log as the routed and On* stages.
	/// </summary>
	public sealed class MarkerRawHandler(ITelegramBotClient bot) : IRawUpdateHandler
	{
		public async Task HandleAsync(Update update, CancellationToken cancellationToken)
		{
			var chatId = update switch
			{
				{ Message: { } message } => message.Chat.Id,
				{ CallbackQuery: { } callback } => callback.Message?.Chat.Id ?? callback.From.Id,
				{ InlineQuery: { } inline } => inline.From.Id,
				{ ChosenInlineResult: { } chosen } => chosen.From.Id,
				_ => 0,
			};

			_ = await bot.SendMessage(
				chatId,
				string.Create(CultureInfo.InvariantCulture, $"raw: update {update.Id}"),
				cancellationToken: cancellationToken);
		}
	}
}
