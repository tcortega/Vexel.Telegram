using Axon.Telegram.Commands.Contexts;

namespace Axon.Telegram.Interactivity.Contexts;

/// <summary>
/// Represents contextual information about a currently executing interaction-based command.
/// </summary>
public interface IInteractionCommandContext : IInteractionContext, ICommandContext;