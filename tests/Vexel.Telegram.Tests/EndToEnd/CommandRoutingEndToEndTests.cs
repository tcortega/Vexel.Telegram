using System.Globalization;
using System.Reflection;
using System.Text;
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
/// End-to-end for the T3 slice, from a bot author's source to a live chat: a bot assembly written
/// with <c>[Handler]</c> + <c>[Command]</c> is compiled with both the Immediate and Vexel
/// generators, loaded, registered through its generated <c>Add{Assembly}Telegram()</c>, and run in a
/// real host against a fake Telegram Bot API. Users then type commands and the assertions are on
/// what Telegram saw the bot send back.
/// </summary>
public sealed class CommandRoutingEndToEndTests(ITestOutputHelper output)
{
	private const string Token = "424242:AAHkQqL2b0zGf4nZ0K2QYQ0F0uSj1sM6Q8w";

	/// <summary>The bot author's own project, exactly as they would write it.</summary>
	private const string BotSource = """
		using System.Threading;
		using System.Threading.Tasks;
		using Immediate.Handlers.Shared;
		using Microsoft.Extensions.DependencyInjection;
		using Vexel.Telegram.Handlers;
		using Vexel.Telegram.Handlers.Attributes;
		using Vexel.Telegram.Handlers.Contexts;

		namespace DemoBot;

		/// <summary>The bot's composition root: Immediate handlers + the generated Vexel routes.</summary>
		public static class BotRegistration
		{
			public static IServiceCollection AddDemoBot(IServiceCollection services) =>
				services
					.AddDemoBotHandlers()
					.AddDemoBotTelegram();
		}

		[Handler]
		[Command("start", Description = "Start the bot")]
		public static partial class Start
		{
			public sealed record Command;

			private static async ValueTask HandleAsync(
				Command command,
				Feedback feedback,
				MessageContext message,
				CancellationToken token)
			{
				_ = await feedback.ReplyAsync(
					$"Welcome, {message.Message.From!.FirstName}!",
					cancellationToken: token);
			}
		}

		[Handler]
		[Command("echo", Description = "Echo the text back")]
		public static partial class Echo
		{
			public sealed record Command(string Text);

			private static async ValueTask HandleAsync(
				Command command,
				Feedback feedback,
				CancellationToken token)
			{
				_ = await feedback.ReplyAsync($"echo: {command.Text}", cancellationToken: token);
			}
		}

		[Handler]
		[Command("add", Description = "Add two numbers")]
		public static partial class Add
		{
			public sealed record Command(int Left, int Right);

			private static async ValueTask HandleAsync(
				Command command,
				Feedback feedback,
				CancellationToken token)
			{
				_ = await feedback.ReplyAsync(
					$"{command.Left} + {command.Right} = {command.Left + command.Right}",
					cancellationToken: token);
			}
		}

		[Handler]
		[Command("say", Description = "Repeat text n times")]
		public static partial class Say
		{
			public sealed record Command(int Times, string Text);

			private static async ValueTask HandleAsync(
				Command command,
				Feedback feedback,
				CancellationToken token)
			{
				_ = await feedback.ReplyAsync(
					string.Join(" ", System.Linq.Enumerable.Repeat(command.Text, command.Times)),
					cancellationToken: token);
			}
		}
		""";

	[Fact]
	public async Task Bot_RoutesTypedCommands_ThroughGeneratedRouteTable_ToImmediateHandlers()
	{
		var transcript = new Transcript();

		// Compile the bot author's project with both generators and load the result, so the routes
		// under test are the generator's own emitted code, not a hand-written stand-in.
		var botAssembly = GeneratorTestHelper.EmitBotAssembly(BotSource, "DemoBot", out var generatedRoutes);
		transcript.Write("compiled DemoBot with Immediate + Vexel generators; loaded generated route table");

		await using var api = new FakeTelegramBotApi(transcript.Write);
		api.Start();
		transcript.Write($"fake Telegram Bot API listening on {api.BaseAddress} (bot username @vexel_bot)");

		using var host = BuildHost(api.BaseAddress, transcript, botAssembly);
		await host.StartAsync();
		await WaitForAsync(() => api.GetUpdatesCalls >= 2);

		var router = host.Services.GetRequiredService<TelegramRouter>();

		var conversation = new (int Id, long ChatId, string Text)[]
		{
			(902, 100, "/start"),
			(903, 100, "/echo hello world"),
			(904, 100, "/add 2 40"),
			(905, 100, "/say 3 ha"),
			(906, 100, "/ECHO case insensitive"),
			(907, 100, "/echo@vexel_bot addressed to me"),
			(908, 100, "/echo@other_bot addressed to another bot"),
			(909, 100, "/add not-a-number"),
			(910, 100, "/unregistered"),
			(911, 100, "plain text, not a command"),
			(912, 200, "/start"),
		};

		foreach (var (id, chatId, text) in conversation)
		{
			transcript.Write(string.Create(
				CultureInfo.InvariantCulture,
				$"user in chat {chatId} sends \"{text}\""));

			api.Enqueue(FakeTelegramBotApi.CommandUpdate(id, chatId, text));
			await WaitForAsync(() => api.Requests.Any(r => r.Contains($",{id}]", StringComparison.Ordinal)
				|| r.EndsWith($"[{id}]", StringComparison.Ordinal)));

			// Give the routed update time to reach (or be rejected by) a handler before the next one.
			await Task.Delay(250);
		}

		// Every command that routes answers exactly once; wait for the last one rather than assuming
		// a fixed settle time is enough on a slow machine.
		await WaitForAsync(() => api.OutboundCalls.Count(
			static c => c.StartsWith("sendMessage", StringComparison.Ordinal)) == 7);

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

		output.WriteLine(string.Empty);
		output.WriteLine("Composed command table (SetMyCommands metadata):");
		foreach (var command in router.CommandMetadata)
		{
			output.WriteLine($"  /{command.Name} - {command.Description}");
		}

		_ = EvidenceWriter.TryWrite(
			"command-routing-e2e.md",
			BuildEvidence(generatedRoutes, transcript, calls, router.CommandMetadata));

		var replies = calls
			.Where(static c => c.StartsWith("sendMessage", StringComparison.Ordinal))
			.ToArray();

		// Argument binding straight off the generated route table: empty, single-string, tokenized
		// primitives, and trailing-string-takes-rest.
		Assert.Contains(replies, r => r.Contains("chat_id=100", StringComparison.Ordinal)
			&& r.Contains("\"Welcome, chat100!\"", StringComparison.Ordinal));
		Assert.Contains(replies, r => r.Contains("\"echo: hello world\"", StringComparison.Ordinal));
		Assert.Contains(replies, r => r.Contains("\"2 + 40 = 42\"", StringComparison.Ordinal));
		Assert.Contains(replies, r => r.Contains("\"ha ha ha\"", StringComparison.Ordinal));

		// Command keys match case-insensitively.
		Assert.Contains(replies, r => r.Contains("\"echo: case insensitive\"", StringComparison.Ordinal));

		// @Bot suffix: this bot's own username routes, another bot's does not.
		Assert.Contains(replies, r => r.Contains("\"echo: addressed to me\"", StringComparison.Ordinal));
		Assert.DoesNotContain(replies, r => r.Contains("addressed to another bot", StringComparison.Ordinal));

		// Unbindable arguments, unknown commands and plain text never reach a handler, and the bot
		// stays silent instead of guessing.
		Assert.DoesNotContain(replies, r => r.Contains("not-a-number", StringComparison.Ordinal));
		Assert.True(transcript.Contains("Failed to bind arguments for /add"));
		Assert.Equal(7, replies.Length);

		// The other chat is routed independently.
		Assert.Contains(replies, r => r.Contains("chat_id=200", StringComparison.Ordinal)
			&& r.Contains("\"Welcome, chat200!\"", StringComparison.Ordinal));

		// Descriptions from [Command] reach the composed SetMyCommands table.
		Assert.Equal(
			["add", "echo", "say", "start"],
			[.. router.CommandMetadata.Select(static c => c.Name)]);
		Assert.Contains(router.CommandMetadata, static c => c is { Name: "start", Description: "Start the bot" });
	}

	private static string BuildEvidence(
		string generatedRoutes,
		Transcript transcript,
		IReadOnlyList<string> outboundCalls,
		IReadOnlyList<BotCommandDescriptor> commandMetadata)
	{
		var writer = new StringBuilder();

		writer.AppendLine("# /command routing, end to end");
		writer.AppendLine();
		writer.AppendLine(
			"A bot assembly written with `[Handler]` + `[Command]` is compiled with the Immediate and");
		writer.AppendLine(
			"Vexel generators, loaded, registered through its generated `AddDemoBotTelegram()`, and run");
		writer.AppendLine(
			"in a real host against a fake Telegram Bot API server over HTTP.");
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

		writer.AppendLine("## 3. Live session: user messages and what Telegram saw the bot send");
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

		writer.AppendLine("## 4. Composed command table (SetMyCommands metadata)");
		writer.AppendLine();
		writer.AppendLine("| Command | Description |");
		writer.AppendLine("| --- | --- |");
		foreach (var command in commandMetadata)
		{
			writer.AppendLine(CultureInfo.InvariantCulture, $"| `/{command.Name}` | {command.Description} |");
		}

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
