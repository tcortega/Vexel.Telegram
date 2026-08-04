using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vexel.Telegram.Handlers.DependencyInjection;
using Vexel.Telegram.Sample;

// Three explicit calls - never hide Immediate or the generated route table:
//   1. AddTelegramBot      - client, host, contexts, Feedback, Flow, router, SetMyCommands
//   2. AddXxxHandlers      - Immediate.Handlers DI for every [Handler]
//   3. AddXxxTelegram      - Vexel-generated route / flow-step / On* contribution
var builder = Host.CreateApplicationBuilder(args);

// The host only wires user-secrets in the Development environment, and this sample runs as
// Production by default - add the provider explicitly so the README's `dotnet run` works as-is.
builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true, reloadOnChange: false);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
	options.IncludeScopes = true;
	options.SingleLine = true;
	options.TimestampFormat = "HH:mm:ss ";
});

var token = ResolveBotToken(builder.Configuration);
builder.Services.AddTelegramBot(_ => token);
builder.Services.AddVexelTelegramSampleHandlers();
builder.Services.AddVexelTelegramSampleTelegram();

var host = builder.Build();
await host.RunAsync();

static string ResolveBotToken(IConfiguration configuration)
{
	// Prefer, in order: env TELEGRAM_BOT_TOKEN, config BotToken (user-secrets / appsettings),
	// config Telegram:BotToken. See README.md for setup. Test-DC wiring lands in T12a.
	var token =
		Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")
		?? configuration["BotToken"]
		?? configuration["Telegram:BotToken"];

	if (string.IsNullOrWhiteSpace(token))
	{
		throw new InvalidOperationException(
			"Bot token missing. Set TELEGRAM_BOT_TOKEN, or `dotnet user-secrets set BotToken <token>` "
			+ "in samples/Vexel.Telegram.Sample. See README.md.");
	}

	return token.Trim();
}
