using Axon.Telegram.Commands.Contexts;
using Telegram.Bot.Types;
using OneOf;

namespace Axon.Telegram.Interactivity.Contexts;

/// <summary>
/// Represents contextual information about an ongoing operation on an interaction.
/// </summary>
public interface IInteractionContext : IOperationContext
{
    /// <summary>
    /// Gets the interaction.
    /// </summary>
    OneOf<CallbackQuery, InlineQuery, ChosenInlineResult> Interaction { get; }

    /// <summary>
    /// Gets the user who initiated the interaction.
    /// </summary>
    User User { get; }
}