using Telegram.Bot.Types;
using OneOf;

namespace Vexel.Telegram.Commands.Extensions;

/// <summary>
/// Extension methods for retrieving chat IDs from various telegram types.
/// </summary>
public static class ContextExtensions
{
	public static long GetChatId(this OneOf<CallbackQuery, InlineQuery, ChosenInlineResult, Message> updates)
	{
		return updates.Match(
			callbackQuery => callbackQuery.Message?.Chat.Id ??
				throw new InvalidOperationException("CallbackQuery does not contain a message with a chat ID."),
			inlineQuery => inlineQuery.From.Id,
			chosenInlineResult => chosenInlineResult.From.Id,
			message => message.Chat.Id
		);
	}
}
