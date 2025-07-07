namespace Vexel.Telegram.Interactivity;

/// <summary>
/// Represents the state of an ongoing conversation, indicating which text response is being awaited.
/// </summary>
public record ConversationState
{
	/// <summary>
	/// Gets the unique identifier of the handler method designated to process the user's text input.
	/// </summary>
	public required string HandlerName { get; init; }

	/// <summary>
	/// Gets the optional state data to be passed to the handler method.
	/// This allows for maintaining context within a multi-step conversation.
	/// </summary>
	public string? State { get; init; }
}
