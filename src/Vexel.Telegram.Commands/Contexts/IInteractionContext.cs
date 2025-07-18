using OneOf;
using Telegram.Bot.Types;

namespace Vexel.Telegram.Commands;

/// <summary>
/// Represents contextual information about an ongoing operation on an interaction.
/// </summary>
public interface IInteractionContext : IOperationContext
{
	/// <summary>
	/// Gets the interaction.
	/// </summary>
	OneOf<CallbackQuery, InlineQuery, ChosenInlineResult, Message> Interaction { get; }

	/// <summary>
	/// Gets the user who initiated the interaction.
	/// </summary>
	User User { get; }
}
