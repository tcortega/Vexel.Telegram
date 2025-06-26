namespace Axon.Telegram.Commands.Services;

/// <summary>
/// Represents the context of a command's execution.
/// </summary>
public interface ICommandContext
{
    /// <summary>
    /// Gets a cancellation token for the current operation.
    /// </summary>
    CancellationToken CancellationToken { get; }
}