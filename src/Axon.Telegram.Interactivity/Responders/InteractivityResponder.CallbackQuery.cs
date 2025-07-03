using Axon.Telegram.Abstractions.Responders;
using Axon.Telegram.Interactivity.Contexts;
using Remora.Results;
using Telegram.Bot.Types;

namespace Axon.Telegram.Interactivity.Responders;

internal partial class InteractivityResponder : IResponder<CallbackQuery>
{
	public Task<Result> RespondAsync(CallbackQuery update, CancellationToken ct = default)
	{
		if (update.Data is null || !InteractionIdHelper.IsFromInteractionTree(update.Data))
		{
			return Task.FromResult(Result.FromSuccess());
		}

		var context = new InteractionContext(update, update.From);
		contextInjection.Context = context;

		var (commandPath, parameters) = ExtractPathAndParameters(update.Data);
		return TryExecuteCommandAsync(context, commandPath, parameters, ct);
	}
}
