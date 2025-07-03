using Vexel.Telegram.Client.Extensions;
using Vexel.Telegram.Commands.Extensions;
using Vexel.Telegram.Commands.Registration;
using Vexel.Telegram.Hosting.Extensions;
using Vexel.Telegram.Interactivity.Extensions;
using Vexel.Telegram.Sample.Commands;
using Vexel.Telegram.Sample.Interactions;
using Vexel.Telegram.Sample.Responders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Remora.Commands.Extensions;

var host = Host.CreateDefaultBuilder(args)
	.AddTelegramService(_ => "7147223926:AAHCusb8jLfbcIahOTefzHz3RMFRPDTt2SU")
	.ConfigureServices((ctx, services) =>
	{
		_ = services.AddTelegramCommands();
		_ = services.AddTelegramInteractivity();

		// Add custom responders
		_ = services.AddResponder<MessageResponder>();

		// Register command groups
		_ = services.AddCommandTree()
			.WithCommandGroup<GeneralCommands>();

		// Register interaction groups
		_ = services.AddInteractionGroup<SampleInteractions>();
	})
	.ConfigureLogging(logging =>
	{
		_ = logging.ClearProviders();
		_ = logging.AddSimpleConsole(options =>
		{
			options.IncludeScopes = true;
			options.SingleLine = true;
			options.TimestampFormat = "hh:mm:ss ";
		});
	})
	.Build();

await using var scope = host.Services.CreateAsyncScope();
var registrar = scope.ServiceProvider.GetService<CommandRegistrar>()!;
await registrar.RegisterCommandsAsync();

await host.RunAsync();
