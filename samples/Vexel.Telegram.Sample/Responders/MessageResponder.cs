using Vexel.Telegram.Abstractions.Responders;
using Microsoft.Extensions.Logging;
using Remora.Results;
using Telegram.Bot.Types;
using Vexel.Telegram.Commands;

namespace Vexel.Telegram.Sample.Responders;

/// <summary>
/// A responder that reacts to new messages.
/// </summary>
public class MessageResponder(ILogger<MessageResponder> logger, IFeedbackService feedbackService)
	: IResponder<Message>
{
	/// <summary>
	/// Handles incoming messages.
	/// </summary>
	public async Task<Result> RespondAsync(Message message, CancellationToken ct = default)
	{
		// We don't want to respond to our own messages or empty messages
		if (string.IsNullOrEmpty(message.Text))
		{
			return Result.FromSuccess();
		}

		logger.LogInformation("Received message from {User}: {Text}", message.From?.Username ?? "Unknown User",
			message.Text);

		// Simple echo logic
		var result = await feedbackService.SendContextualMessageAsync
		(
			$"Here's a hello from the MessageResponder: {message.Text}",
			ct: ct
		);

		if (result.IsSuccess) return Result.FromSuccess();
		logger.LogError("Failed to send message: {Error}", result.Error);
		return Result.FromError(result.Error);
	}
}
