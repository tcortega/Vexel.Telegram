using Telegram.Bot.Types;

namespace Vexel.Telegram.Handlers.Contexts;

/// <summary>
/// Contextual information for a <see cref="Update.Message"/> update.
/// </summary>
/// <param name="Message">The inbound message.</param>
public sealed record MessageContext(Message Message)
{
	/// <summary>Chat that received the message. Always present for message updates.</summary>
	public long ChatId => Message.Chat.Id;

	/// <summary>Sender user id when Telegram included <see cref="Message.From"/>.</summary>
	public long? UserId => Message.From?.Id;

	/// <summary>Id of the inbound message.</summary>
	public int MessageId => Message.Id;
}
