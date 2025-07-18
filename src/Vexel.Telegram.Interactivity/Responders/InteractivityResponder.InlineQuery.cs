using Vexel.Telegram.Abstractions.Responders;
using Remora.Results;
using Telegram.Bot.Types;
using Vexel.Telegram.Commands;

namespace Vexel.Telegram.Interactivity;

internal partial class InteractivityResponder : IResponder<InlineQuery>
{
	public Task<Result> RespondAsync(InlineQuery update, CancellationToken ct = default)
	{
		if (!InteractionIdHelper.IsFromInteractionTree(update.Id))
		{
			return Task.FromResult(Result.FromSuccess());
		}

		var context = new InteractionContext(update, update.From);
		contextInjection.Context = context;

		var (commandPath, parameters) = ExtractPathAndParameters(update.Id);
		return TryExecuteCommandAsync(context, commandPath, parameters, ct);
	}
}
