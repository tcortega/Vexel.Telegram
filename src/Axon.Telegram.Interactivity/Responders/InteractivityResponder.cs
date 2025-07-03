using Axon.Telegram.Commands.Contexts;
using Axon.Telegram.Commands.Execution;
using Axon.Telegram.Commands.Extensions;
using Axon.Telegram.Interactivity.Contexts;
using Microsoft.Extensions.Options;
using Remora.Commands.Services;
using Remora.Commands.Tokenization;
using Remora.Commands.Trees;
using Remora.Results;
using PathAndParameters =
    (string[] CommandPath,
    System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<string>> Parameters);

namespace Axon.Telegram.Interactivity.Responders;

internal sealed partial class InteractivityResponder(
    CommandService commandService,
    IServiceProvider services,
    ContextInjectionService contextInjection,
    IOptions<TokenizerOptions> tokenizerOptions,
    IOptions<TreeSearchOptions> treeSearchOptions,
    ExecutionEventCollectorService eventCollector
)
{
    private readonly TokenizerOptions _tokenizerOptions = tokenizerOptions.Value;
    private readonly TreeSearchOptions _treeSearchOptions = treeSearchOptions.Value;

    private async Task<Result> TryExecuteCommandAsync
    (
        InteractionContext operationContext,
        IReadOnlyList<string> commandPath,
        IReadOnlyDictionary<string, IReadOnlyList<string>> parameters,
        CancellationToken ct = default
    )
    {
        var prepareCommand = await commandService.TryPrepareCommandAsync
        (
            commandPath,
            parameters,
            services,
            searchOptions: _treeSearchOptions,
            tokenizerOptions: _tokenizerOptions,
            treeName: Constants.InteractionTree,
            ct: ct
        );

        if (!prepareCommand.IsSuccess)
        {
            var preparationError = await eventCollector.RunPreparationErrorEvents
            (
                services,
                operationContext,
                prepareCommand,
                ct
            );

            if (!preparationError.IsSuccess)
            {
                return preparationError;
            }

            if (prepareCommand.Error.IsUserOrEnvironmentError())
            {
                return Result.FromSuccess();
            }

            return (Result)prepareCommand;
        }

        var preparedCommand = prepareCommand.Entity;

        var commandContext =
            new InteractionCommandContext(operationContext.Interaction, operationContext.User, preparedCommand, ct);

        contextInjection.Context = commandContext;
        var preExecution = await eventCollector.RunPreExecutionEvents(services, commandContext, ct);
        if (!preExecution.IsSuccess)
        {
            return preExecution;
        }

        var executionResult = await commandService.TryExecuteAsync(preparedCommand, services, ct);

        return await eventCollector.RunPostExecutionEvents
        (
            services,
            commandContext,
            executionResult.IsSuccess ? executionResult.Entity : executionResult,
            ct
        );
    }

    private static PathAndParameters ExtractPathAndParameters(string interactionId)
    {
        var commandPath = interactionId[Constants.InteractionTree.Length..][2..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        commandPath = ExtractState(commandPath, out var state);

        var parameters = new Dictionary<string, IReadOnlyList<string>>();
        if (state is not null)
        {
            parameters.Add("state", [state]);
        }

        return (commandPath, parameters);
    }

    private static string[] ExtractState(string[] commandPath, out string? state)
    {
        if (commandPath.Length <= 0 || !commandPath[0].StartsWith(Constants.StatePrefix))
        {
            state = null;
            return commandPath;
        }

        state = commandPath[0][Constants.StatePrefix.Length..];
        return commandPath[1..];
    }
}