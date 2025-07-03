using Remora.Commands.Services;
using Telegram.Bot.Types;

namespace Vexel.Telegram.Commands.Contexts;

/// <summary>
/// Represents contextual information about a currently executing text-based command.
/// </summary>
public interface ITextCommandContext : IMessageContext, ICommandContext;

/// <summary>
/// A command context specific to Telegram, containing the message that triggered the command.
/// </summary>
/// <param name="Message">The message that triggered the command.</param>
/// <param name="Command">The command associated with the context.</param>
/// <param name="CancellationToken">The cancellation token associated with the context.</param>
public sealed record TextCommandContext(
	Message Message,
	PreparedCommand Command,
	CancellationToken CancellationToken) : ITextCommandContext;
