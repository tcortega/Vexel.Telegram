using Telegram.Bot.Types;

namespace Vexel.Telegram.Handlers.Contexts;

/// <summary>
/// Contextual information for a <see cref="Update.CallbackQuery"/> update.
/// Chat is nullable: inline-origin callbacks have no <see cref="CallbackQuery.Message"/>.
/// </summary>
/// <param name="CallbackQuery">The inbound callback query.</param>
public sealed record CallbackContext(CallbackQuery CallbackQuery)
{
	/// <summary>Opaque callback query id used with <c>answerCallbackQuery</c>.</summary>
	public string Id => CallbackQuery.Id;

	/// <summary>User who pressed the button.</summary>
	public User From => CallbackQuery.From;

	/// <summary>Developer-provided callback data, if any.</summary>
	public string? Data => CallbackQuery.Data;

	/// <summary>Message the button belongs to; null for inline-origin callbacks.</summary>
	public Message? Message => CallbackQuery.Message;

	/// <summary>
	/// Chat id when the callback originated from a chat message; null for inline-origin callbacks
	/// (same rule as the dispatch lane key: <c>Message?.Chat.Id</c>).
	/// </summary>
	public long? ChatId => CallbackQuery.Message?.Chat.Id;

	/// <summary>Message id when a chat message is present.</summary>
	public int? MessageId => CallbackQuery.Message?.Id;

	/// <summary>Inline message id for callbacks from inline-mode messages.</summary>
	public string? InlineMessageId => CallbackQuery.InlineMessageId;
}
