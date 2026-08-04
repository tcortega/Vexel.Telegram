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
/// End-to-end for the T7 slice, from a bot author's source to a live inline session: a bot assembly
/// written with <c>[Handler]</c> + <c>[InlineQuery]</c> + <c>[ChosenInlineResult]</c> is compiled
/// with both the Immediate and Vexel generators, loaded, registered through its generated
/// <c>Add{Assembly}Telegram()</c>, and run in a real host against a fake Telegram Bot API. A user
/// then types <c>@vexel_bot ...</c> in another chat and picks a result, and the assertions are on
/// what Telegram saw the bot send back - including the fail-closed empty
/// <c>answerInlineQuery</c> obligation.
/// </summary>
public sealed class InlineRoutingEndToEndTests(ITestOutputHelper output)
{
	private const string Token = "424242:AAHkQqL2b0zGf4nZ0K2QYQ0F0uSj1sM6Q8w";

	/// <summary>The bot author's own project, exactly as they would write it.</summary>
	private const string BotSource = """
		using System;
		using System.Threading;
		using System.Threading.Tasks;
		using Immediate.Handlers.Shared;
		using Microsoft.Extensions.DependencyInjection;
		using Telegram.Bot.Types.InlineQueryResults;
		using Vexel.Telegram.Handlers;
		using Vexel.Telegram.Handlers.Attributes;

		namespace InlineBot;

		/// <summary>The bot's composition root: Immediate handlers + the generated Vexel routes.</summary>
		public static class BotRegistration
		{
			public static IServiceCollection AddInlineBot(IServiceCollection services) =>
				services
					.AddInlineBotHandlers()
					.AddInlineBotTelegram();
		}

		/// <summary>Trigger route: the query text after "search" binds to the string parameter.</summary>
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
				// ResultId carries the chosen-inline-result route key, same key|suffix shape as callbacks.
				await feedback.AnswerInlineAsync(
					[
						new InlineQueryResultArticle(
							$"item|{query.Text}",
							$"Search: {query.Text}",
							new InputTextMessageContent($"You searched for {query.Text}")),
					],
					cacheTime: 30,
					cancellationToken: token);
			}
		}

		/// <summary>Default route: empty and unmatched queries land here with the full query text.</summary>
		[Handler]
		[InlineQuery]
		public static partial class DefaultInline
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
							"hint",
							$"Default: {query.Text}",
							new InputTextMessageContent("Try: search <term>")),
					],
					cacheTime: 30,
					cancellationToken: token);
			}
		}

		/// <summary>A trigger whose handler faults before answering.</summary>
		[Handler]
		[InlineQuery("boom")]
		public static partial class BoomInline
		{
			public sealed record Query(string Text);

			private static ValueTask HandleAsync(Query query, CancellationToken token) =>
				throw new InvalidOperationException("inline handler blew up");
		}

		/// <summary>ResultId prefix route: the suffix after '|' binds to the string parameter.</summary>
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
				await feedback.EditAsync($"Picked \"{command.Term}\" - loading...", cancellationToken: token);
			}
		}
		""";

	/// <summary>A bot with only a trigger route, so unmatched queries have nowhere to land.</summary>
	private const string MinimalBotSource = """
		using System.Threading;
		using System.Threading.Tasks;
		using Immediate.Handlers.Shared;
		using Microsoft.Extensions.DependencyInjection;
		using Telegram.Bot.Types.InlineQueryResults;
		using Vexel.Telegram.Handlers;
		using Vexel.Telegram.Handlers.Attributes;

		namespace MinimalBot;

		public static class BotRegistration
		{
			public static IServiceCollection AddMinimalBot(IServiceCollection services) =>
				services
					.AddMinimalBotHandlers()
					.AddMinimalBotTelegram();
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
							"only",
							$"Search: {query.Text}",
							new InputTextMessageContent(query.Text)),
					],
					cancellationToken: token);
			}
		}
		""";

	[Fact]
	public async Task Bot_RoutesInlineQueriesOnText_PicksUpChosenResults_AndAlwaysAnswers()
	{
		var transcript = new Transcript();

		// Compile the bot author's project with both generators and load the result, so the inline
		// routes under test are the generator's own emitted code, not a hand-written stand-in.
		var botAssembly = GeneratorTestHelper.EmitBotAssembly(BotSource, "InlineBot", out var generatedRoutes);
		transcript.Write("compiled InlineBot with Immediate + Vexel generators; loaded generated route table");

		await using var api = new FakeTelegramBotApi(transcript.Write);
		api.Start();
		transcript.Write($"fake Telegram Bot API listening on {api.BaseAddress} (bot username @vexel_bot)");

		using var host = BuildHost(api.BaseAddress, transcript, botAssembly, "AddInlineBot");
		await host.StartAsync();
		await WaitForAsync(() => api.GetUpdatesCalls >= 2);

		// Everything the user types into the inline field. The third case is the trap: the opaque
		// inline_query_id is literally "search", so a router that keyed off the id instead of the
		// text would hit the search trigger.
		var typings = new (int UpdateId, string InlineQueryId, string Query, string What)[]
		{
			(970, "iq-1", "search kittens", "types \"@vexel_bot search kittens\""),
			(971, "iq-2", "hello there", "types \"@vexel_bot hello there\" (no trigger matches)"),
			(972, "search", "kittens", "types \"@vexel_bot kittens\" - Telegram's inline_query_id happens to be \"search\""),
			(973, "iq-4", "", "opens the inline field with an empty query"),
			(974, "iq-5", "boom", "types \"@vexel_bot boom\", whose handler throws"),
		};

		foreach (var (updateId, inlineQueryId, query, what) in typings)
		{
			transcript.Write(string.Create(CultureInfo.InvariantCulture, $"user 200 {what}"));

			api.Enqueue(FakeTelegramBotApi.InlineQueryUpdate(updateId, 200, inlineQueryId, query));

			// Telegram considers the query handled once the bot answers it.
			await WaitForAsync(() => api.OutboundCalls.Any(
				c => c.Contains($"inline_query_id=\"{inlineQueryId}\"", StringComparison.Ordinal)));
		}

		// The user taps the article the "search kittens" answer offered, so Telegram posts it and
		// reports the chosen result id back to the bot.
		transcript.Write("user 200 taps the \"Search: kittens\" result (result_id \"item|kittens\")");
		api.Enqueue(FakeTelegramBotApi.ChosenInlineResultUpdate(
			975,
			200,
			"item|kittens",
			"search kittens",
			"im-9001"));
		await WaitForAsync(() => api.OutboundCalls.Any(
			static c => c.Contains("inline_message_id=\"im-9001\"", StringComparison.Ordinal)));

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
			"inline-routing-e2e.md",
			BuildEvidence(generatedRoutes, transcript, calls));

		var answers = calls
			.Where(static c => c.StartsWith("answerInlineQuery", StringComparison.Ordinal))
			.ToArray();

		// Trigger matched on the first whitespace token; the remainder bound to the string parameter.
		Assert.Contains(
			answers,
			static a => a.Contains("inline_query_id=\"iq-1\"", StringComparison.Ordinal)
				&& a.Contains("results=[item|kittens -> \"Search: kittens\"]", StringComparison.Ordinal));

		// Unmatched query fell through to the empty-trigger default with the full query text.
		Assert.Contains(
			answers,
			static a => a.Contains("inline_query_id=\"iq-2\"", StringComparison.Ordinal)
				&& a.Contains("results=[hint -> \"Default: hello there\"]", StringComparison.Ordinal));

		// Never routes on InlineQuery.Id: the id "search" did not select the "search" trigger, the
		// query text "kittens" did the routing and landed on the default.
		Assert.Contains(
			answers,
			static a => a.Contains("inline_query_id=\"search\"", StringComparison.Ordinal)
				&& a.Contains("results=[hint -> \"Default: kittens\"]", StringComparison.Ordinal));
		Assert.DoesNotContain(
			answers,
			static a => a.Contains("inline_query_id=\"search\"", StringComparison.Ordinal)
				&& a.Contains("Search: kittens", StringComparison.Ordinal));

		// Empty query is the default handler's other entry point, with an empty payload.
		Assert.Contains(
			answers,
			static a => a.Contains("inline_query_id=\"iq-4\"", StringComparison.Ordinal)
				&& a.Contains("results=[hint -> \"Default: \"]", StringComparison.Ordinal));

		// Fail-closed: the throwing handler never answered, so the obligation sent the default empty
		// result set with cache_time 0 and the user's inline field stopped spinning.
		Assert.Contains(
			answers,
			static a => a.Contains("inline_query_id=\"iq-5\"", StringComparison.Ordinal)
				&& a.Contains("results=[]", StringComparison.Ordinal)
				&& a.Contains("cache_time=0", StringComparison.Ordinal));

		// Exactly one answer per query: the obligation never doubles up on an app answer.
		Assert.Equal(typings.Length, answers.Length);

		// The picked result routed on its ResultId prefix, with "kittens" bound from the suffix, and
		// edited the message the inline result posted.
		Assert.Contains(
			calls,
			static c => c.StartsWith("editMessageText", StringComparison.Ordinal)
				&& c.Contains("inline_message_id=\"im-9001\"", StringComparison.Ordinal)
				&& c.Contains("text=\"Picked \"kittens\" - loading...\"", StringComparison.Ordinal));

		Assert.True(transcript.Contains("inline handler blew up"));
	}

	[Fact]
	public async Task Bot_WithoutDefaultTrigger_StillAnswersUnroutedInlineQueries()
	{
		var transcript = new Transcript();

		var botAssembly = GeneratorTestHelper.EmitBotAssembly(MinimalBotSource, "MinimalBot", out var generatedRoutes);
		transcript.Write("compiled MinimalBot (only an [InlineQuery(\"search\")] route, no default)");

		await using var api = new FakeTelegramBotApi(transcript.Write);
		api.Start();

		using var host = BuildHost(api.BaseAddress, transcript, botAssembly, "AddMinimalBot");
		await host.StartAsync();
		await WaitForAsync(() => api.GetUpdatesCalls >= 2);

		transcript.Write("user 300 types \"@vexel_bot weather london\" - no route claims it");
		api.Enqueue(FakeTelegramBotApi.InlineQueryUpdate(980, 300, "iq-unrouted", "weather london"));
		await WaitForAsync(() => api.OutboundCalls.Any(
			static c => c.Contains("inline_query_id=\"iq-unrouted\"", StringComparison.Ordinal)));

		await host.StopAsync();

		var calls = api.OutboundCalls;

		foreach (var line in transcript.Lines)
		{
			output.WriteLine(line);
		}

		_ = EvidenceWriter.TryWrite(
			"inline-unrouted-e2e.md",
			BuildUnroutedEvidence(generatedRoutes, transcript, calls));

		var answer = Assert.Single(
			calls,
			static c => c.StartsWith("answerInlineQuery", StringComparison.Ordinal));

		// Nothing routed and nothing answered, so the obligation still closed the query out.
		Assert.Contains("inline_query_id=\"iq-unrouted\"", answer, StringComparison.Ordinal);
		Assert.Contains("results=[]", answer, StringComparison.Ordinal);
		Assert.Contains("cache_time=0", answer, StringComparison.Ordinal);
	}

	private static string BuildEvidence(
		string generatedRoutes,
		Transcript transcript,
		IReadOnlyList<string> outboundCalls)
	{
		var writer = new StringBuilder();

		writer.AppendLine("# `[InlineQuery]` / `[ChosenInlineResult]` routing and the inline answer obligation, end to end");
		writer.AppendLine();
		writer.AppendLine(
			"A bot assembly written with `[Handler]` + `[InlineQuery]` + `[ChosenInlineResult]` is");
		writer.AppendLine(
			"compiled with the Immediate and Vexel generators, loaded, registered through its generated");
		writer.AppendLine(
			"`AddInlineBotTelegram()`, and run in a real host against a fake Telegram Bot API server over");
		writer.AppendLine("HTTP. A user then types into the inline field and picks a result.");
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

		writer.AppendLine("## 3. Live session: what the user typed and what Telegram saw the bot send");
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

		writer.AppendLine("### What each inline query produced");
		writer.AppendLine();
		writer.AppendLine("| Typed | inline_query_id | Route | Answer |");
		writer.AppendLine("| --- | --- | --- | --- |");
		writer.AppendLine(
			"| `search kittens` | `iq-1` | `[InlineQuery(\"search\")]`, remainder `kittens` bound | app results `Search: kittens` |");
		writer.AppendLine(
			"| `hello there` | `iq-2` | no trigger matches -> `[InlineQuery]` default, full text bound | app results `Default: hello there` |");
		writer.AppendLine(
			"| `kittens` | `search` | id looks like a trigger and is ignored -> default, `kittens` bound | app results `Default: kittens` |");
		writer.AppendLine(
			"| *(empty)* | `iq-4` | `[InlineQuery]` default, empty payload | app results `Default: ` |");
		writer.AppendLine(
			"| `boom` | `iq-5` | `[InlineQuery(\"boom\")]`, handler throws | default empty answer, `cache_time=0`, from the obligation |");
		writer.AppendLine();
		writer.AppendLine(
			"Then `chosen_inline_result` with `result_id` `item|kittens` routed to");
		writer.AppendLine(
			"`[ChosenInlineResult(\"item\")]` with `kittens` bound from the suffix, and edited the posted");
		writer.AppendLine("inline message (`inline_message_id=\"im-9001\"`).");
		writer.AppendLine();

		return writer.ToString();
	}

	private static string BuildUnroutedEvidence(
		string generatedRoutes,
		Transcript transcript,
		IReadOnlyList<string> outboundCalls)
	{
		var writer = new StringBuilder();

		writer.AppendLine("# Fail-closed inline answer for an unrouted query");
		writer.AppendLine();
		writer.AppendLine(
			"A bot that registers only `[InlineQuery(\"search\")]` and no default handler. The user types");
		writer.AppendLine(
			"something no route claims, so nothing runs and nothing answers - and the B4 obligation still");
		writer.AppendLine("closes the query out with an empty result set and `cache_time=0`.");
		writer.AppendLine();

		writer.AppendLine("## Bot source");
		writer.AppendLine();
		writer.AppendLine("```csharp");
		writer.AppendLine(MinimalBotSource);
		writer.AppendLine("```");
		writer.AppendLine();

		writer.AppendLine("## Generated route table (`Vexel.Telegram.Routes.g.cs`)");
		writer.AppendLine();
		writer.AppendLine("```csharp");
		writer.AppendLine(generatedRoutes.TrimEnd());
		writer.AppendLine("```");
		writer.AppendLine();

		writer.AppendLine("## Live session");
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
		string registrationMethod)
	{
		var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

		_ = builder.Logging.SetMinimumLevel(LogLevel.Information);
		_ = builder.Services.AddSingleton<ILoggerProvider>(_ => new TranscriptLoggerProvider(transcript));

		_ = builder.Services.AddSingleton<ITelegramBotClient>(
			_ => new TelegramBotClient(new TelegramBotClientOptions(Token, apiAddress)));

		// Vexel host wiring plus the bot's own composition root
		// (Add{Bot}Handlers() + generated Add{Bot}Telegram()).
		_ = builder.Services.AddTelegramBot(_ => Token);
		Invoke(botAssembly, registrationMethod, builder.Services);

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
