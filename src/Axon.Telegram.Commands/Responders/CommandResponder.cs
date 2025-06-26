using Axon.Telegram.Commands.Services;
using Axon.Telegram.Commands.Services.Execution;
using Axon.Telegram.Responders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Remora.Commands.Services;
using Remora.Results;
using Telegram.Bot.Types;

namespace Axon.Telegram.Commands.Responders;

/// <summary>
/// A responder that listens for messages and executes commands.
/// </summary>
public class CommandResponder(
    ILogger<CommandResponder> logger,
    CommandService commandService,
    ContextInjectionService contextInjector,
    ExecutionEventCollectorService eventCollector,
    IServiceProvider services, // We keep the service provider to pass to the context.
    IOptions<CommandOptions> options)
    : IResponder<Message>
{
    private readonly CommandOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<Result> RespondAsync(Message message, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(message.Text) || !message.Text.StartsWith(_options.Prefix))
        {
            return Result.FromSuccess();
        }

        var commandText = message.Text[_options.Prefix.Length..];

        // 1. Set the context on the scoped injection service. This makes the context available
        //    to any other service within this same scope (e.g., post-execution events).
        contextInjector.Context = new TelegramCommandContext(message, ct);

        // 2. The command is executed using the correct, per-update service provider.
        //    The CommandService is injected directly into our constructor.
        var result = await commandService.TryExecuteAsync(commandText, services, ct: ct);

        // 3. All post-execution logic is delegated to the collector service, which is also
        //    injected directly. We pass the IServiceProvider for event handlers to use.
        await eventCollector.RunPostExecutionEventsAsync
        (
            services,
            contextInjector.Context,
            result.IsSuccess ? result.Entity : result,
            ct
        );

        // 4. The original result from the command execution is returned.
        if (result.IsSuccess) return Result.FromSuccess();

        logger.LogTrace("Command execution failed: {Error}", result.Error.Message);
        return Result.FromError(result.Error);
    }
}