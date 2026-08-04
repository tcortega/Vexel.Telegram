using Telegram.Bot.Types;
using Vexel.Telegram.Client.Dispatch;

namespace Vexel.Telegram.Handlers.Contexts;

/// <summary>
/// Binds <see cref="UpdateContextHolder"/> at the start of each update scope.
/// </summary>
public sealed class UpdateContextScopeInitializer(UpdateContextHolder holder) : IUpdateScopeInitializer
{
	/// <inheritdoc />
	public void Initialize(Update update) => holder.Set(update);
}
