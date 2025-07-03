using Remora.Commands.Services;

namespace Axon.Telegram.Commands.Contexts;

/// <summary>
/// Represents the context of a command's execution.
/// </summary>
public interface ICommandContext : IOperationContext
{
	/// <summary>
	/// Gets the command associated with the context.
	/// </summary>
	PreparedCommand Command { get; }

	/// <summary>
	/// Gets a cancellation token for the current operation.
	/// </summary>
	CancellationToken CancellationToken { get; }
}
