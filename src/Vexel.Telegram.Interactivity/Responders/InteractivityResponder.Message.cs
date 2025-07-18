using Remora.Results;
using Telegram.Bot.Types;
using Vexel.Telegram.Abstractions.Responders;
using Vexel.Telegram.Commands.Contexts;

namespace Vexel.Telegram.Interactivity;

internal partial class InteractivityResponder : IResponder<Message>
{
	public async Task<Result> RespondAsync(Message update, CancellationToken ct = default)
	{
		if (string.IsNullOrWhiteSpace(update.Text) || update.From is null)
		{
			return Result.FromSuccess();
		}

		var stateResult = conversationStateService.TryGetAwaitingInput(update.Chat.Id, update.From.Id);
		if (!stateResult.IsSuccess)
		{
			return Result.FromSuccess();
		}

		var context = new InteractionContext(update, update.From);
		contextInjection.Context = context;

		var state = stateResult.Entity;
		var updateId = InteractionIdHelper.CreateTextResponseId(state.HandlerName, state.State);
		var (commandPath, parameters) = ExtractPathAndParameters(updateId);
		parameters.Add("input", [update.Text]);

		return await TryExecuteCommandAsync(context, commandPath, parameters, ct);
	}
}
