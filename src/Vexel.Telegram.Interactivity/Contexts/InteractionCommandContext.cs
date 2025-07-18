using OneOf;
using Remora.Commands.Services;
using Telegram.Bot.Types;
using Vexel.Telegram.Commands;

namespace Vexel.Telegram.Interactivity;

/// <summary>
/// Represents contextual information about an ongoing command operation on an interaction.
/// </summary>
/// <param name="Interaction">The actual update payload.</param>
/// <param name="User">The user who triggered the interaction.</param>
/// <param name="Command">The command associated with the context.</param>
public record InteractionCommandContext(
	OneOf<CallbackQuery, InlineQuery, ChosenInlineResult, Message> Interaction,
	User User,
	PreparedCommand Command
) : InteractionContext(Interaction, User), IInteractionCommandContext;
