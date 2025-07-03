using Telegram.Bot.Types;

namespace Axon.Telegram.Commands.Contexts;

/// <summary>
/// Represents contextual information about an ongoing operation on a message.
/// </summary>
public interface IMessageContext : IOperationContext
{
	/// <summary>
	/// Gets the message associated with the context.
	/// </summary>
	Message Message { get; }
}

/// <summary>
/// Represents contextual information about an ongoing operation on a message.
/// </summary>
/// <param name="Message">The message.</param>
public sealed record MessageContext(Message Message) : IOperationContext;
