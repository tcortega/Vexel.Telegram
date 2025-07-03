using Axon.Telegram.Abstractions.Responders;
using Microsoft.Extensions.Logging;
using Remora.Results;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Axon.Telegram.Sample.Responders;

/// <summary>
/// A responder that reacts to new messages.
/// </summary>
public class MessageResponder(ILogger<MessageResponder> logger, ITelegramBotClient botClient)
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
		_ = await botClient.SendMessage
		(
			chatId: message.Chat.Id,
			text: $"Here's a hello from the MessageResponder: {message.Text}",
			cancellationToken: ct
		);

		return Result.FromSuccess();
	}
}
