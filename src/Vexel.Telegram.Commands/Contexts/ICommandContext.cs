using Remora.Commands.Services;

namespace Vexel.Telegram.Commands;

/// <summary>
/// Represents the context of a command's execution.
/// </summary>
public interface ICommandContext : IOperationContext
{
	/// <summary>
	/// Gets the command associated with the context.
	/// </summary>
	PreparedCommand Command { get; }
}
