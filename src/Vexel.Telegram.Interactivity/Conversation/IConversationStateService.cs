using Remora.Results;

namespace Vexel.Telegram.Interactivity;

/// <summary>
/// Defines a service for managing the state of ongoing user conversations.
/// </summary>
public interface IConversationStateService
{
	/// <summary>
	/// Sets a user in a specific chat to a "waiting for input" state.
	/// </summary>
	/// <param name="chatId">The ID of the chat.</param>
	/// <param name="userId">The ID of the user.</param>
	/// <param name="handlerName">The unique identifier for the expected response, matching a [TextResponse] attribute.</param>
	/// <param name="expiry">How long to wait for the user's input before the state expires.</param>
	void SetAwaitingInput(long chatId, long userId, string handlerName, string? state = null, TimeSpan? expiry = null);

	/// <summary>
	/// Tries to retrieve and remove the awaited response ID for a given user and chat.
	/// </summary>
	/// <param name="chatId">The ID of the chat.</param>
	/// <param name="userId">The ID of the user.</param>
	/// <returns>A result which may or may not have succeeded.</returns>
	Result<ConversationState> TryGetAwaitingInput(long chatId, long userId);
}
