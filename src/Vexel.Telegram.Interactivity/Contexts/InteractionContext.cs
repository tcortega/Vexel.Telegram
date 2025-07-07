using OneOf;
using Telegram.Bot.Types;

namespace Vexel.Telegram.Interactivity;

/// <summary>
/// Represents contextual information about an ongoing operation on an interaction.
/// </summary>
/// <param name="Interaction">The actual update payload.</param>
/// <param name="User">The user who triggered the interaction.</param>
public record InteractionContext(OneOf<CallbackQuery, InlineQuery, ChosenInlineResult, Message> Interaction, User User)
	: IInteractionContext;
