using Remora.Results;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Vexel.Telegram.Commands.Feedback;

/// <summary>
/// Handles sending formatted messages to the users.
/// </summary>
public interface IFeedbackService
{
	/// <summary>
	/// Send a contextual message.
	/// </summary>
	/// <param name="message">The message to send.</param>
	/// <param name="options">The message options to use.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	Task<Result<Message>> SendContextualMessageAsync
	(
		string message,
		ParseMode parseMode = default,
		FeedbackMessageOptions? options = null,
		CancellationToken ct = default
	);
}
