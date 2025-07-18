using Remora.Results;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

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
	/// <param name="parseMode">The parse mode for formatting.</param>
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

	/// <summary>
	/// Send a message to a specific chat.
	/// </summary>
	/// <param name="chatId">The chat to send the message to.</param>
	/// <param name="message">The message to send.</param>
	/// <param name="parseMode">The parse mode for formatting.</param>
	/// <param name="options">The message options to use.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	Task<Result<Message>> SendMessageAsync
	(
		ChatId chatId,
		string message,
		ParseMode parseMode = default,
		FeedbackMessageOptions? options = null,
		CancellationToken ct = default
	);

	/// <summary>
	/// Send a photo contextually.
	/// </summary>
	/// <param name="photo">The photo to send.</param>
	/// <param name="caption">The caption for the photo.</param>
	/// <param name="parseMode">The parse mode for the caption.</param>
	/// <param name="options">The message options to use.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	Task<Result<Message>> SendContextualPhotoAsync
	(
		InputFile photo,
		string? caption = null,
		ParseMode parseMode = default,
		FeedbackMessageOptions? options = null,
		CancellationToken ct = default
	);

	/// <summary>
	/// Send a document contextually.
	/// </summary>
	/// <param name="document">The document to send.</param>
	/// <param name="caption">The caption for the document.</param>
	/// <param name="parseMode">The parse mode for the caption.</param>
	/// <param name="options">The message options to use.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	Task<Result<Message>> SendContextualDocumentAsync
	(
		InputFile document,
		string? caption = null,
		ParseMode parseMode = default,
		FeedbackMessageOptions? options = null,
		CancellationToken ct = default
	);

	/// <summary>
	/// Edit a message contextually.
	/// </summary>
	/// <param name="messageId">The ID of the message to edit.</param>
	/// <param name="newText">The new text for the message.</param>
	/// <param name="parseMode">The parse mode for formatting.</param>
	/// <param name="replyMarkup">The inline keyboard markup.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	Task<Result<Message>> EditContextualMessageAsync
	(
		int messageId,
		string newText,
		ParseMode parseMode = default,
		InlineKeyboardMarkup? replyMarkup = null,
		CancellationToken ct = default
	);

	/// <summary>
	/// Delete a message contextually.
	/// </summary>
	/// <param name="messageId">The ID of the message to delete.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	Task<Result> DeleteContextualMessageAsync
	(
		int messageId,
		CancellationToken ct = default
	);

	/// <summary>
	/// Send a success message contextually.
	/// </summary>
	/// <param name="message">The success message to send.</param>
	/// <param name="options">The message options to use.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	Task<Result<Message>> SendContextualSuccessAsync
	(
		string message,
		FeedbackMessageOptions? options = null,
		CancellationToken ct = default
	);

	/// <summary>
	/// Send an error message contextually.
	/// </summary>
	/// <param name="message">The error message to send.</param>
	/// <param name="options">The message options to use.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	Task<Result<Message>> SendContextualErrorAsync
	(
		string message,
		FeedbackMessageOptions? options = null,
		CancellationToken ct = default
	);

	/// <summary>
	/// Send a warning message contextually.
	/// </summary>
	/// <param name="message">The warning message to send.</param>
	/// <param name="options">The message options to use.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	Task<Result<Message>> SendContextualWarningAsync
	(
		string message,
		FeedbackMessageOptions? options = null,
		CancellationToken ct = default
	);

	/// <summary>
	/// Send an info message contextually.
	/// </summary>
	/// <param name="message">The info message to send.</param>
	/// <param name="options">The message options to use.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	Task<Result<Message>> SendContextualInfoAsync
	(
		string message,
		FeedbackMessageOptions? options = null,
		CancellationToken ct = default
	);

	/// <summary>
	/// Send a private message to a user.
	/// </summary>
	/// <param name="userId">The user to send the message to.</param>
	/// <param name="message">The message to send.</param>
	/// <param name="parseMode">The parse mode for formatting.</param>
	/// <param name="options">The message options to use.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	Task<Result<Message>> SendPrivateMessageAsync
	(
		long userId,
		string message,
		ParseMode parseMode = default,
		FeedbackMessageOptions? options = null,
		CancellationToken ct = default
	);

	/// <summary>
	/// Reply to a specific message contextually.
	/// </summary>
	/// <param name="replyToMessageId">The ID of the message to reply to.</param>
	/// <param name="message">The reply message.</param>
	/// <param name="parseMode">The parse mode for formatting.</param>
	/// <param name="options">The message options to use.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	Task<Result<Message>> ReplyToMessageAsync
	(
		int replyToMessageId,
		string message,
		ParseMode parseMode = default,
		FeedbackMessageOptions? options = null,
		CancellationToken ct = default
	);

	/// <summary>
	/// Forward a message.
	/// </summary>
	/// <param name="toChatId">The chat to forward the message to.</param>
	/// <param name="fromChatId">The chat to forward the message from.</param>
	/// <param name="messageId">The ID of the message to forward.</param>
	/// <param name="disableNotification">Whether to disable notification.</param>
	/// <param name="protectContent">Whether to protect content.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	Task<Result<Message>> ForwardMessageAsync
	(
		ChatId toChatId,
		ChatId fromChatId,
		int messageId,
		bool disableNotification = false,
		bool protectContent = false,
		CancellationToken ct = default
	);

	/// <summary>
	/// Send a typing action contextually.
	/// </summary>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	Task<Result> SendContextualTypingAsync(CancellationToken ct = default);

	/// <summary>
	/// Send a video contextually.
	/// </summary>
	/// <param name="video">The video to send.</param>
	/// <param name="caption">The caption for the video.</param>
	/// <param name="parseMode">The parse mode for the caption.</param>
	/// <param name="options">The message options to use.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	Task<Result<Message>> SendContextualVideoAsync
	(
		InputFile video,
		string? caption = null,
		ParseMode parseMode = default,
		FeedbackMessageOptions? options = null,
		CancellationToken ct = default
	);

	/// <summary>
	/// Send an audio contextually.
	/// </summary>
	/// <param name="audio">The audio to send.</param>
	/// <param name="caption">The caption for the audio.</param>
	/// <param name="parseMode">The parse mode for the caption.</param>
	/// <param name="options">The message options to use.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	Task<Result<Message>> SendContextualAudioAsync
	(
		InputFile audio,
		string? caption = null,
		ParseMode parseMode = default,
		FeedbackMessageOptions? options = null,
		CancellationToken ct = default
	);
}
