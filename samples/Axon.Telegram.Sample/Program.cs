using Axon.Telegram.Client.Extensions;
using Axon.Telegram.Commands.Extensions;
using Axon.Telegram.Commands.Registration;
using Axon.Telegram.Hosting.Extensions;
using Axon.Telegram.Interactivity.Extensions;
using Axon.Telegram.Sample.Commands;
using Axon.Telegram.Sample.Interactions;
using Axon.Telegram.Sample.Responders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Remora.Commands.Extensions;

var host = Host.CreateDefaultBuilder(args)
    .AddTelegramService(_ => "<TOKEN_HERE>")
    .ConfigureServices((_, services) =>
    {
        services.AddTelegramCommands();
        services.AddTelegramInteractivity();

        // Add custom responders
        services.AddResponder<MessageResponder>();

        // Register command groups
        services.AddCommandTree()
            .WithCommandGroup<GeneralCommands>();

        // Register interaction groups
        services.AddInteractionGroup<SampleInteractions>();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(options =>
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