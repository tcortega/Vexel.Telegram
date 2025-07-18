using Vexel.Telegram.Commands;
using Vexel.Telegram.Commands.Contexts;

namespace Vexel.Telegram.Interactivity;

/// <summary>
/// Represents contextual information about a currently executing interaction-based command.
/// </summary>
public interface IInteractionCommandContext : IInteractionContext, ICommandContext;
