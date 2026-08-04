using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Vexel.Telegram.AspNetCore.Extensions;
using Vexel.Telegram.Client;
using Vexel.Telegram.Client.Webhook;
using Vexel.Telegram.Handlers.DependencyInjection;
using Vexel.Telegram.Tests.Fakes;
using Vexel.Telegram.Tests.Generators;
using Xunit.Abstractions;

namespace Vexel.Telegram.Tests.EndToEnd;

/// <summary>
/// End-to-end for the T9 host slice, from a bot author's source to a live deployment: a bot
/// compiled with the Vexel generator runs in a real ASP.NET Core app in webhook mode against a fake
/// Telegram Bot API, publishes its command menu at start, and its ingress endpoint is exercised with
/// real HTTP requests the way Telegram (and a forger) would send them.
/// </summary>
public sealed class WebhookHostEndToEndTests(ITestOutputHelper output)
{
	private const string Token = "424242:AAHkQqL2b0zGf4nZ0K2QYQ0F0uSj1sM6Q8w";
	private const string Secret = "vexel-webhook-secret_01";
	private const string WebhookPath = "/telegram/webhook";

	/// <summary>The bot author's own project, exactly as they would write it.</summary>
	private const string BotSource = """
		using System.Threading;
		using System.Threading.Tasks;
		using Immediate.Handlers.Shared;
		using Microsoft.Extensions.DependencyInjection;
		using Vexel.Telegram.Handlers;
		using Vexel.Telegram.Handlers.Attributes;
		using Vexel.Telegram.Handlers.Contexts;

		namespace WebhookBot;

		public static class BotRegistration
		{
			public static IServiceCollection AddWebhookBot(IServiceCollection services) =>
				services
					.AddWebhookBotHandlers()
					.AddWebhookBotTelegram();
		}

		[Handler]
		[Command("start", Description = "Start the bot")]
		public static partial class Start
		{
			public sealed record Command;

			private static async ValueTask HandleAsync(
				Command command,
				Feedback feedback,
				CancellationToken token)
			{
				_ = await feedback.ReplyAsync("Welcome!", cancellationToken: token);
			}
		}

		[Handler]
		[Command("weather", Description = "Weather for a city")]
		public static partial class Weather
		{
			public sealed record Command(string City);

			private static async ValueTask HandleAsync(
				Command command,
				Feedback feedback,
				CancellationToken token)
			{
				_ = await feedback.ReplyAsync($"{command.City}: sunny", cancellationToken: token);
			}
		}
		""";

	/// <summary>The host wiring the bot author writes, reproduced verbatim by this test.</summary>
	private const string HostSource = """
		var builder = WebApplication.CreateSlimBuilder(args);

		builder.Services.AddTelegramBot(
			_ => botToken,
			options =>
			{
				options.ReceiveMode = TelegramReceiveMode.Webhook;
				options.Webhook = new WebhookOptions
				{
					Url = new Uri("https://bot.example.test/telegram/webhook"),
					SecretToken = "vexel-webhook-secret_01",
					Path = "/telegram/webhook",
				};
			});

		builder.Services.AddWebhookBot();   // AddWebhookBotHandlers() + generated AddWebhookBotTelegram()

		var app = builder.Build();

		app.MapTelegramWebhook();           // explicit opt-in; nothing is auto-mapped

		app.Run();
		""";

	private static readonly Lazy<(Assembly Assembly, string GeneratedRoutes)> s_bot = new(() =>
	{
		var assembly = GeneratorTestHelper.EmitBotAssembly(BotSource, "WebhookBot", out var routes);
		return (assembly, routes);
	});

	[Fact]
	public async Task WebhookBot_PublishesCommandMenu_SetsSecret_AndOnlyAcceptsAuthenticDeliveries()
	{
		var transcript = new Transcript();
		var http = new List<string>();

		var (botAssembly, generatedRoutes) = s_bot.Value;
		transcript.Write("compiled WebhookBot with Immediate + Vexel generators; loaded generated route table");

		await using var api = new FakeTelegramBotApi(transcript.Write);
		api.Start();
		transcript.Write($"fake Telegram Bot API listening on {api.BaseAddress}");

		await using var app = BuildWebhookApp(api.BaseAddress, transcript, botAssembly, registerBotCommands: true);
		await app.StartAsync();

		var origin = app.Urls.First();
		transcript.Write($"bot app listening on {origin}, webhook endpoint mapped at {WebhookPath}");

		// The host publishes the menu and registers the webhook before any delivery arrives.
		await WaitForAsync(() => api.OutboundCalls.Any(
			static c => c.StartsWith("setMyCommands", StringComparison.Ordinal))
			&& api.OutboundCalls.Any(static c => c.StartsWith("setWebhook", StringComparison.Ordinal)));

		using var client = new HttpClient { BaseAddress = new Uri(origin) };

		// 1. Telegram delivers with the secret it was handed on setWebhook.
		var (_, authentic) = FakeTelegramBotApi.CommandUpdate(701, 100, "/weather Lisbon");
		http.Add(await PostAsync(client, transcript, WebhookPath, authentic, Secret,
			"Telegram delivers /weather Lisbon with the correct secret_token header"));

		await WaitForAsync(() => api.OutboundCalls.Any(
			static c => c.Contains("Lisbon: sunny", StringComparison.Ordinal)));

		// 2. A forger who found the public URL but not the secret.
		var (_, forged) = FakeTelegramBotApi.CommandUpdate(702, 100, "/start");
		http.Add(await PostAsync(client, transcript, WebhookPath, forged, secret: null,
			"attacker POSTs /start with no secret_token header"));
		http.Add(await PostAsync(client, transcript, WebhookPath, forged, "wrong-secret",
			"attacker POSTs /start with a wrong secret_token header"));

		// 3. Nothing is auto-mapped: only the path the app asked for exists.
		http.Add(await PostAsync(client, transcript, "/", forged, Secret,
			"POST to an unmapped path (proves no auto-mapped route)"));

		// Give any (wrongly) accepted forgery time to reach a handler before the log is read.
		await Task.Delay(500);

		await app.StopAsync();
		transcript.Write("app stopped");

		var calls = api.OutboundCalls;

		foreach (var line in transcript.Lines)
		{
			output.WriteLine(line);
		}

		output.WriteLine(string.Empty);
		output.WriteLine("Inbound HTTP to the bot's webhook endpoint:");
		foreach (var line in http)
		{
			output.WriteLine($"  {line}");
		}

		output.WriteLine(string.Empty);
		output.WriteLine("Bot -> Telegram Bot API calls, as the Telegram side saw them:");
		foreach (var call in calls)
		{
			output.WriteLine($"  {call}");
		}

		_ = EvidenceWriter.TryWrite(
			"webhook-host-e2e.md",
			BuildEvidence(generatedRoutes, transcript, http, calls));

		// The command menu is published from the generated route table, descriptions and all.
		var setMyCommands = Assert.Single(
			calls,
			static c => c.StartsWith("setMyCommands", StringComparison.Ordinal));
		Assert.Contains("/start - \"Start the bot\"", setMyCommands, StringComparison.Ordinal);
		Assert.Contains("/weather - \"Weather for a city\"", setMyCommands, StringComparison.Ordinal);

		// setWebhook carries the URL and the secret Telegram must echo back.
		var setWebhook = Assert.Single(
			calls,
			static c => c.StartsWith("setWebhook", StringComparison.Ordinal));
		Assert.Contains("url=\"https://bot.example.test/telegram/webhook\"", setWebhook, StringComparison.Ordinal);
		Assert.Contains($"secret_token=\"{Secret}\"", setWebhook, StringComparison.Ordinal);

		// The authentic delivery is accepted and routed; the forgeries are rejected with 403 and
		// never reach a handler.
		Assert.Equal(
			[
				$"POST {WebhookPath} (secret_token: correct) -> 200 OK",
				$"POST {WebhookPath} (secret_token: absent) -> 403 Forbidden",
				$"POST {WebhookPath} (secret_token: wrong) -> 403 Forbidden",
				"POST / (secret_token: correct) -> 404 NotFound",
			],
			http);

		Assert.Contains(calls, static c => c.Contains("\"Lisbon: sunny\"", StringComparison.Ordinal));
		Assert.DoesNotContain(calls, static c => c.Contains("\"Welcome!\"", StringComparison.Ordinal));

		// The mapped path matches the public URL path, so no proxy-rewrite warning is logged.
		Assert.False(transcript.Contains("differs from the mapped endpoint path"));
	}

	[Fact]
	public async Task WebhookBot_WithRegisterBotCommandsFalse_PublishesNoMenu()
	{
		var transcript = new Transcript();
		var (botAssembly, _) = s_bot.Value;

		await using var api = new FakeTelegramBotApi(transcript.Write);
		api.Start();

		await using var app = BuildWebhookApp(api.BaseAddress, transcript, botAssembly, registerBotCommands: false);
		await app.StartAsync();

		// setWebhook still runs, so waiting on it proves start-up got past the point where
		// setMyCommands would have been sent.
		await WaitForAsync(() => api.OutboundCalls.Any(
			static c => c.StartsWith("setWebhook", StringComparison.Ordinal)));

		await app.StopAsync();

		var calls = api.OutboundCalls;

		output.WriteLine("RegisterBotCommands = false; Bot -> Telegram Bot API calls:");
		foreach (var call in calls)
		{
			output.WriteLine($"  {call}");
		}

		var writer = new StringBuilder();
		writer.AppendLine("# SetMyCommands opt-out (`VexelClientOptions.RegisterBotCommands = false`)");
		writer.AppendLine();
		writer.AppendLine(
			"The same bot, with the same two `[Command]`-annotated handlers, started with the opt-out");
		writer.AppendLine("flag set. Telegram sees no `setMyCommands`, so the BotFather menu is left alone.");
		writer.AppendLine();
		writer.AppendLine("Bot -> Telegram Bot API calls, in order:");
		writer.AppendLine();
		writer.AppendLine("```text");
		foreach (var call in calls)
		{
			writer.AppendLine(call);
		}

		writer.AppendLine("```");
		_ = EvidenceWriter.TryWrite("setmycommands-opt-out-e2e.md", writer.ToString());

		Assert.DoesNotContain(calls, static c => c.StartsWith("setMyCommands", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Host_ConfiguredForBothPollingAndWebhook_FailsFastAtStart()
	{
		var transcript = new Transcript();
		var bot = new RecordingTelegramBotClient();

		using var host = BuildConsoleHost(transcript, bot, options =>
		{
			options.ReceiveMode = TelegramReceiveMode.Polling;
			options.Webhook = new WebhookOptions
			{
				Url = new Uri("https://bot.example.test/telegram/webhook"),
				SecretToken = Secret,
			};
		});

		var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
		var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var registration = lifetime.ApplicationStopping.Register(() => stopping.TrySetResult());

		// The misconfiguration is caught before any network call, so the host never reaches a
		// running state: it tears itself down at start instead of serving with a dead receive loop.
		await host.StartAsync();
		await stopping.Task.WaitAsync(TimeSpan.FromSeconds(20));
		transcript.Write("host application lifetime signalled ApplicationStopping at start (no zombie host)");

		await StopAsync(host, transcript);

		foreach (var line in transcript.Lines)
		{
			output.WriteLine(line);
		}

		_ = EvidenceWriter.TryWrite(
			"host-fail-fast-e2e.md",
			BuildFailFastEvidence("Both polling and webhook configured", transcript));

		Assert.True(transcript.Contains("both polling and webhook"));
		Assert.True(transcript.Contains("Fatal error while running the Vexel Telegram client"));
		Assert.True(lifetime.ApplicationStopping.IsCancellationRequested);

		// No receive path was ever opened: no getUpdates, no setWebhook.
		Assert.Empty(bot.Requests);
	}

	[Fact]
	public async Task Host_WhenClientFailsFatally_StopsInsteadOfRunningAsAZombie()
	{
		var transcript = new Transcript();
		var bot = new DelayedFaultTelegramBotClient(
			static request => request is DeleteWebhookRequest
				? new HttpRequestException("Telegram API unreachable")
				: null);

		using var host = BuildConsoleHost(transcript, bot, static _ => { });

		await host.StartAsync();
		transcript.Write("host started; polling receive loop is running");

		var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
		var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var registration = lifetime.ApplicationStopping.Register(() => stopping.TrySetResult());

		await stopping.Task.WaitAsync(TimeSpan.FromSeconds(20));
		transcript.Write("host application lifetime signalled ApplicationStopping (no zombie host)");

		await StopAsync(host, transcript);

		foreach (var line in transcript.Lines)
		{
			output.WriteLine(line);
		}

		_ = EvidenceWriter.TryWrite(
			"host-fatal-error-e2e.md",
			BuildFailFastEvidence("Fatal client error while receiving", transcript));

		Assert.True(transcript.Contains("Fatal error while running the Vexel Telegram client"));
		Assert.True(lifetime.ApplicationStopping.IsCancellationRequested);
	}

	private static async Task<string> PostAsync(
		HttpClient client,
		Transcript transcript,
		string path,
		string updateJson,
		string? secret,
		string what)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, path)
		{
			Content = JsonContent.Create(System.Text.Json.JsonDocument.Parse(updateJson).RootElement),
		};

		if (secret is not null)
		{
			request.Headers.Add(WebhookOptions.SecretTokenHeaderName, secret);
		}

		transcript.Write(what);
		using var response = await client.SendAsync(request);

		var label = secret switch
		{
			null => "absent",
			Secret => "correct",
			_ => "wrong",
		};

		var line = string.Create(
			CultureInfo.InvariantCulture,
			$"POST {path} (secret_token: {label}) -> {(int)response.StatusCode} {response.StatusCode}");
		transcript.Write($"webhook endpoint responded {(int)response.StatusCode} {response.StatusCode}");

		return line;
	}

	private static WebApplication BuildWebhookApp(
		string apiAddress,
		Transcript transcript,
		Assembly botAssembly,
		bool registerBotCommands)
	{
		var builder = WebApplication.CreateSlimBuilder();

		_ = builder.WebHost.UseUrls("http://127.0.0.1:0");
		_ = builder.Logging.SetMinimumLevel(LogLevel.Information);
		_ = builder.Services.AddSingleton<ILoggerProvider>(_ => new TranscriptLoggerProvider(transcript));

		_ = builder.Services.AddSingleton<ITelegramBotClient>(
			_ => new TelegramBotClient(new TelegramBotClientOptions(Token, apiAddress)));

		_ = builder.Services.AddTelegramBot(
			_ => Token,
			options =>
			{
				options.RegisterBotCommands = registerBotCommands;
				options.ReceiveMode = TelegramReceiveMode.Webhook;
				options.Webhook = new WebhookOptions
				{
					Url = new Uri("https://bot.example.test/telegram/webhook"),
					SecretToken = Secret,
					Path = WebhookPath,
				};
			});

		Invoke(botAssembly, "AddWebhookBot", builder.Services);

		var app = builder.Build();
		_ = app.MapTelegramWebhook();

		return app;
	}

	private static IHost BuildConsoleHost(
		Transcript transcript,
		ITelegramBotClient bot,
		Action<VexelClientOptions> configure)
	{
		var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

		_ = builder.Logging.SetMinimumLevel(LogLevel.Information);
		_ = builder.Services.AddSingleton<ILoggerProvider>(_ => new TranscriptLoggerProvider(transcript));
		_ = builder.Services.AddSingleton(bot);
		_ = builder.Services.AddTelegramBot(_ => Token, configure);

		return builder.Build();
	}

	private static string BuildEvidence(
		string generatedRoutes,
		Transcript transcript,
		IReadOnlyList<string> httpCalls,
		IReadOnlyList<string> outboundCalls)
	{
		var writer = new StringBuilder();

		writer.AppendLine("# Webhook receive + automatic SetMyCommands, end to end");
		writer.AppendLine();
		writer.AppendLine(
			"A bot written with `[Handler]` + `[Command]` is compiled with the Immediate and Vexel");
		writer.AppendLine(
			"generators and run in a real ASP.NET Core app (Kestrel on loopback) in webhook mode against");
		writer.AppendLine(
			"a fake Telegram Bot API server. Deliveries are real HTTP POSTs to the mapped endpoint.");
		writer.AppendLine();

		writer.AppendLine("## 1. What the bot author wrote");
		writer.AppendLine();
		writer.AppendLine("```csharp");
		writer.AppendLine(BotSource);
		writer.AppendLine("```");
		writer.AppendLine();
		writer.AppendLine("```csharp");
		writer.AppendLine(HostSource);
		writer.AppendLine("```");
		writer.AppendLine();

		writer.AppendLine("## 2. Route table the Vexel generator emitted (`Vexel.Telegram.Routes.g.cs`)");
		writer.AppendLine();
		writer.AppendLine("```csharp");
		writer.AppendLine(generatedRoutes.TrimEnd());
		writer.AppendLine("```");
		writer.AppendLine();

		writer.AppendLine("## 3. Inbound HTTP, as Telegram (and a forger) sent it");
		writer.AppendLine();
		writer.AppendLine("| Request | Result |");
		writer.AppendLine("| --- | --- |");
		foreach (var call in httpCalls)
		{
			var split = call.LastIndexOf(" -> ", StringComparison.Ordinal);
			writer.AppendLine(CultureInfo.InvariantCulture, $"| `{call[..split]}` | `{call[(split + 4)..]}` |");
		}

		writer.AppendLine();

		writer.AppendLine("## 4. Bot -> Telegram Bot API calls, in order");
		writer.AppendLine();
		writer.AppendLine("```text");
		foreach (var call in outboundCalls)
		{
			writer.AppendLine(call);
		}

		writer.AppendLine("```");
		writer.AppendLine();

		writer.AppendLine("## 5. Live session log");
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

	private static string BuildFailFastEvidence(string scenario, Transcript transcript)
	{
		var writer = new StringBuilder();

		writer.AppendLine(CultureInfo.InvariantCulture, $"# Host fail-fast: {scenario}");
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

	/// <summary>
	/// Stops a host that has already torn itself down after a fatal client error. .NET 11's
	/// <c>Host.StopAsync</c> rethrows the background service fault that caused the shutdown, while
	/// .NET 10 only logs it; the host is down either way, so record it instead of failing.
	/// </summary>
	private static async Task StopAsync(IHost host, Transcript transcript)
	{
		try
		{
			await host.StopAsync();
		}
		catch (Exception ex)
		{
			transcript.Write($"host shutdown surfaced the fatal error: {ex.GetType().Name}: {ex.Message}");
		}
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
	/// Fails a request only after yielding, so the fault surfaces once the receive loop is already
	/// running rather than synchronously inside <c>StartAsync</c>.
	/// </summary>
	private sealed class DelayedFaultTelegramBotClient(Func<object, Exception?> fail) : ITelegramBotClient
	{
		public bool LocalBotServer => false;

		public long BotId => 424242;

		public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

		public IExceptionParser ExceptionsParser { get; set; } = new DefaultExceptionParser();

		public event AsyncEventHandler<ApiRequestEventArgs>? OnMakingApiRequest
		{
			add { }
			remove { }
		}

		public event AsyncEventHandler<ApiResponseEventArgs>? OnApiResponseReceived
		{
			add { }
			remove { }
		}

		public async Task<TResponse> SendRequest<TResponse>(
			IRequest<TResponse> request,
			CancellationToken cancellationToken = default)
		{
			await Task.Delay(50, cancellationToken);

			if (fail(request!) is { } failure)
			{
				throw failure;
			}

			return default!;
		}

		public Task<bool> TestApi(CancellationToken cancellationToken = default) => Task.FromResult(true);

		public Task DownloadFile(string filePath, Stream destination, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;

		public Task DownloadFile(TGFile file, Stream destination, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;
	}
}
