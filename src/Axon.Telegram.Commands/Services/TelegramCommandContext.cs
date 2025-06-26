using Telegram.Bot.Types;

namespace Axon.Telegram.Commands.Services;

/// <summary>
/// A command context specific to Telegram, containing the message that triggered the command.
/// </summary>
public sealed class TelegramCommandContext : ICommandContext
{
    /// <summary>
    /// Gets the message that triggered the command.
    /// </summary>
    public Message Message { get; }

    /// <summary>
    /// A cancellation token for the current operation.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TelegramCommandContext"/> class.
    /// </summary>
    /// <param name="message">The message that triggered the command.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public TelegramCommandContext(Message message, CancellationToken cancellationToken)
    {
        Message = message;
        CancellationToken = cancellationToken;
    }
}