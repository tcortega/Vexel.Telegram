using Vexel.Telegram.Abstractions.Responders;
using Vexel.Telegram.Interactivity.Contexts;
using Remora.Results;
using Telegram.Bot.Types;

namespace Vexel.Telegram.Interactivity.Responders;

internal partial class InteractivityResponder : IResponder<ChosenInlineResult>
{
	public Task<Result> RespondAsync(ChosenInlineResult update, CancellationToken ct = default)
	{
		if (!InteractionIdHelper.IsFromInteractionTree(update.ResultId))
		{
			return Task.FromResult(Result.FromSuccess());
		}

		var context = new InteractionContext(update, update.From);
		contextInjection.Context = context;

		var (commandPath, parameters) = ExtractPathAndParameters(update.ResultId);
		return TryExecuteCommandAsync(context, commandPath, parameters, ct);
	}
}
