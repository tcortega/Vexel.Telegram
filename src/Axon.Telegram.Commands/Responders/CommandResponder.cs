using Axon.Telegram.Abstractions.Responders;
using Axon.Telegram.Commands.Contexts;
using Axon.Telegram.Commands.Execution;
using Axon.Telegram.Commands.Extensions;
using Axon.Telegram.Commands.Prefix;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Remora.Commands.Services;
using Remora.Commands.Tokenization;
using Remora.Commands.Trees;
using Remora.Results;
using Telegram.Bot.Types;

namespace Axon.Telegram.Commands.Responders;

/// <summary>
/// A responder that listens for messages and executes commands.
/// </summary>
public class CommandResponder(
	CommandService commandService,
	ContextInjectionService contextInjector,
	IServiceProvider services,
	IOptions<TokenizerOptions> tokenizerOptions,
	IOptions<TreeSearchOptions> treeSearchOptions)
	: IResponder<Message>
{
	private readonly TokenizerOptions _tokenizerOptions = tokenizerOptions.Value;
	private readonly TreeSearchOptions _treeSearchOptions = treeSearchOptions.Value;

	/// <inheritdoc />
	public async Task<Result> RespondAsync(Message message, CancellationToken ct = default)
	{
		if (string.IsNullOrWhiteSpace(message.Text))
		{
			return Result.FromSuccess();
		}

		var context = new MessageContext(message);
		contextInjector.Context = context;

		var prefixMatcher = services.GetRequiredService<ICommandPrefixMatcher>();
		var checkPrefix = await prefixMatcher.MatchesPrefixAsync(message.Text, ct);
		if (!checkPrefix.IsDefined(out var check))
		{
			return (Result)checkPrefix;
		}

		if (!check.Matches)
		{
			return Result.FromSuccess();
		}

		var content = message.Text[check.ContentStartIndex..];

		return await TryExecuteCommandAsync(context, content, ct);
	}

	private async Task<Result> TryExecuteCommandAsync(MessageContext context, string content,
		CancellationToken ct)
	{
		var prepareCommand = await commandService.TryPrepareCommandAsync
		(
			content,
			services,
			_tokenizerOptions,
			_treeSearchOptions,
			null,
			ct
		);

		if (!prepareCommand.IsSuccess)
		{
			var preparationError = await ExecutionEventCollectorService.RunPreparationErrorEvents
			(
				services,
				context,
				prepareCommand,
				ct
			);

			if (!preparationError.IsSuccess && !Equals(preparationError.Error, prepareCommand.Error))
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

		var commandContext = new TextCommandContext(context.Message, preparedCommand, ct);
		contextInjector.Context = commandContext;

		var preExecution = await ExecutionEventCollectorService.RunPreExecutionEvents(services, commandContext, ct);
		if (!preExecution.IsSuccess)
		{
			return preExecution;
		}

		var executionResult = await commandService.TryExecuteAsync(preparedCommand, services, ct);

		return await ExecutionEventCollectorService.RunPostExecutionEvents
		(
			services,
			commandContext,
			executionResult.IsSuccess ? executionResult.Entity : executionResult,
			ct
		);
	}
}
