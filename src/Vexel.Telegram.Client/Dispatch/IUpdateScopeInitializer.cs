using Telegram.Bot.Types;

namespace Vexel.Telegram.Client.Dispatch;

/// <summary>
/// Hook invoked once per update after the dispatch scope is opened and before handlers resolve.
/// Handlers package uses this to bind the write-once <c>UpdateContextHolder</c>.
/// </summary>
public interface IUpdateScopeInitializer
{
	/// <summary>
	/// Initializes scoped update state for <paramref name="update"/>.
	/// </summary>
	/// <param name="update">The inbound Telegram update.</param>
	void Initialize(Update update);
}
