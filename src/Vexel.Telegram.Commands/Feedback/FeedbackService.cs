using Remora.Results;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Vexel.Telegram.Commands;

/// <inheritdoc />
public class FeedbackService(ContextInjectionService contextInjection, ITelegramBotClient botClient) : IFeedbackService
{
	private const string SuccessPrefix = "✅ ";
	private const string ErrorPrefix = "❌ ";
	private const string WarningPrefix = "⚠️ ";
	private const string InfoPrefix = "ℹ️ ";

	/// <inheritdoc />
	public async Task<Result<Message>> SendContextualMessageAsync(string message, ParseMode parseMode = default,
		FeedbackMessageOptions? options = null, CancellationToken ct = default)
	{
		var chatId = GetContextualChatId();
		if (!chatId.IsSuccess)
		{
			return Result<Message>.FromError(chatId.Error);
		}

		return await SendMessageAsync(chatId.Entity, message, parseMode, options, ct);
	}

	/// <inheritdoc />
	public async Task<Result<Message>> SendMessageAsync(ChatId chatId, string message, ParseMode parseMode = default,
		FeedbackMessageOptions? options = null, CancellationToken ct = default)
	{
		try
		{
			if (options is null)
			{
				return await botClient.SendMessage(chatId, message, parseMode, cancellationToken: ct);
			}

			return await botClient.SendMessage(
				chatId: chatId,
				text: message,
				parseMode: parseMode,
				replyParameters: options.ReplyParameters,
				replyMarkup: options.ReplyMarkup,
				linkPreviewOptions: options.LinkPreviewOptions,
				messageThreadId: options.MessageThreadId,
				disableNotification: options.DisableNotification,
				protectContent: options.ProtectContent,
				messageEffectId: options.MessageEffectId,
				businessConnectionId: options.BusinessConnectionId,
				allowPaidBroadcast: options.AllowPaidBroadcast,
				cancellationToken: ct
			);
		}
		catch (Exception ex)
		{
			return new ExceptionError(ex, "Failed to send message.");
		}
	}

	/// <inheritdoc />
	public async Task<Result<Message>> SendContextualPhotoAsync(InputFile photo, string? caption = null, ParseMode parseMode = default,
		FeedbackMessageOptions? options = null, CancellationToken ct = default)
	{
		var chatId = GetContextualChatId();
		if (!chatId.IsSuccess)
		{
			return Result<Message>.FromError(chatId.Error);
		}

		try
		{
			if (options is null)
			{
				return await botClient.SendPhoto(chatId.Entity, photo, caption: caption, parseMode: parseMode, cancellationToken: ct);
			}

			return await botClient.SendPhoto(
				chatId: chatId.Entity,
				photo: photo,
				caption: caption,
				parseMode: parseMode,
				replyParameters: options.ReplyParameters,
				replyMarkup: options.ReplyMarkup,
				messageThreadId: options.MessageThreadId,
				showCaptionAboveMedia: options.ShowCaptionAboveMedia,
				hasSpoiler: options.HasSpoiler,
				disableNotification: options.DisableNotification,
				protectContent: options.ProtectContent,
				messageEffectId: options.MessageEffectId,
				businessConnectionId: options.BusinessConnectionId,
				allowPaidBroadcast: options.AllowPaidBroadcast,
				cancellationToken: ct
			);
		}
		catch (Exception ex)
		{
			return new ExceptionError(ex, "Failed to send photo.");
		}
	}

	/// <inheritdoc />
	public async Task<Result<Message>> SendContextualDocumentAsync(InputFile document, string? caption = null, ParseMode parseMode = default,
		FeedbackMessageOptions? options = null, CancellationToken ct = default)
	{
		var chatId = GetContextualChatId();
		if (!chatId.IsSuccess)
		{
			return Result<Message>.FromError(chatId.Error);
		}

		try
		{
			if (options is null)
			{
				return await botClient.SendDocument(chatId.Entity, document, caption: caption, parseMode: parseMode, cancellationToken: ct);
			}

			return await botClient.SendDocument(
				chatId: chatId.Entity,
				document: document,
				caption: caption,
				parseMode: parseMode,
				replyParameters: options.ReplyParameters,
				replyMarkup: options.ReplyMarkup,
				thumbnail: options.Thumbnail,
				messageThreadId: options.MessageThreadId,
				disableContentTypeDetection: options.DisableContentTypeDetection,
				disableNotification: options.DisableNotification,
				protectContent: options.ProtectContent,
				messageEffectId: options.MessageEffectId,
				businessConnectionId: options.BusinessConnectionId,
				allowPaidBroadcast: options.AllowPaidBroadcast,
				cancellationToken: ct
			);
		}
		catch (Exception ex)
		{
			return new ExceptionError(ex, "Failed to send document.");
		}
	}

	/// <inheritdoc />
	public async Task<Result<Message>> EditContextualMessageAsync(int messageId, string newText, ParseMode parseMode = default,
		InlineKeyboardMarkup? replyMarkup = null, CancellationToken ct = default)
	{
		var chatId = GetContextualChatId();
		if (!chatId.IsSuccess)
		{
			return Result<Message>.FromError(chatId.Error);
		}

		try
		{
			return await botClient.EditMessageText(chatId.Entity, messageId, newText, parseMode, replyMarkup: replyMarkup,
				cancellationToken: ct);
		}
		catch (Exception ex)
		{
			return new ExceptionError(ex, "Failed to edit message.");
		}
	}

	/// <inheritdoc />
	public async Task<Result> DeleteContextualMessageAsync(int messageId, CancellationToken ct = default)
	{
		var chatId = GetContextualChatId();
		if (!chatId.IsSuccess)
		{
			return Result.FromError(chatId.Error);
		}

		try
		{
			await botClient.DeleteMessage(chatId.Entity, messageId, ct);
			return Result.FromSuccess();
		}
		catch (Exception ex)
		{
			return new ExceptionError(ex, "Failed to delete message.");
		}
	}

	/// <inheritdoc />
	public Task<Result<Message>> SendContextualSuccessAsync(string message, FeedbackMessageOptions? options = null, CancellationToken ct = default)
	{
		return SendContextualMessageAsync(SuccessPrefix + message, ParseMode.None, options, ct);
	}

	/// <inheritdoc />
	public Task<Result<Message>> SendContextualErrorAsync(string message, FeedbackMessageOptions? options = null, CancellationToken ct = default)
	{
		return SendContextualMessageAsync(ErrorPrefix + message, ParseMode.None, options, ct);
	}

	/// <inheritdoc />
	public Task<Result<Message>> SendContextualWarningAsync(string message, FeedbackMessageOptions? options = null, CancellationToken ct = default)
	{
		return SendContextualMessageAsync(WarningPrefix + message, ParseMode.None, options, ct);
	}

	/// <inheritdoc />
	public Task<Result<Message>> SendContextualInfoAsync(string message, FeedbackMessageOptions? options = null, CancellationToken ct = default)
	{
		return SendContextualMessageAsync(InfoPrefix + message, ParseMode.None, options, ct);
	}

	/// <inheritdoc />
	public Task<Result<Message>> SendPrivateMessageAsync(long userId, string message, ParseMode parseMode = default,
		FeedbackMessageOptions? options = null, CancellationToken ct = default)
	{
		return SendMessageAsync(new ChatId(userId), message, parseMode, options, ct);
	}

	/// <inheritdoc />
	public async Task<Result<Message>> ReplyToMessageAsync(int replyToMessageId, string message, ParseMode parseMode = default,
		FeedbackMessageOptions? options = null, CancellationToken ct = default)
	{
		var chatId = GetContextualChatId();
		if (!chatId.IsSuccess)
		{
			return Result<Message>.FromError(chatId.Error);
		}

		var replyOptions = options ?? new FeedbackMessageOptions();
		var updatedOptions = replyOptions with { ReplyParameters = new() { MessageId = replyToMessageId } };

		return await SendMessageAsync(chatId.Entity, message, parseMode, updatedOptions, ct);
	}

	/// <inheritdoc />
	public async Task<Result<Message>> ForwardMessageAsync(ChatId toChatId, ChatId fromChatId, int messageId,
		bool disableNotification = false, bool protectContent = false, CancellationToken ct = default)
	{
		try
		{
			return await botClient.ForwardMessage(toChatId, fromChatId, messageId,
				disableNotification: disableNotification, protectContent: protectContent, cancellationToken: ct);
		}
		catch (Exception ex)
		{
			return new ExceptionError(ex, "Failed to forward message.");
		}
	}

	/// <inheritdoc />
	public async Task<Result> SendContextualTypingAsync(CancellationToken ct = default)
	{
		var chatId = GetContextualChatId();
		if (!chatId.IsSuccess)
		{
			return Result.FromError(chatId.Error);
		}

		try
		{
			await botClient.SendChatAction(chatId.Entity, ChatAction.Typing, cancellationToken: ct);
			return Result.FromSuccess();
		}
		catch (Exception ex)
		{
			return new ExceptionError(ex, "Failed to send typing action.");
		}
	}

	/// <summary>
	/// Send a video contextually.
	/// </summary>
	/// <param name="video">The video to send.</param>
	/// <param name="caption">The caption for the video.</param>
	/// <param name="parseMode">The parse mode for the caption.</param>
	/// <param name="options">The message options to use.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	public async Task<Result<Message>> SendContextualVideoAsync(InputFile video, string? caption = null, ParseMode parseMode = default,
		FeedbackMessageOptions? options = null, CancellationToken ct = default)
	{
		var chatId = GetContextualChatId();
		if (!chatId.IsSuccess)
		{
			return Result<Message>.FromError(chatId.Error);
		}

		try
		{
			if (options is null)
			{
				return await botClient.SendVideo(chatId.Entity, video, caption: caption, parseMode: parseMode, cancellationToken: ct);
			}

			return await botClient.SendVideo(
				chatId: chatId.Entity,
				video: video,
				caption: caption,
				parseMode: parseMode,
				replyParameters: options.ReplyParameters,
				replyMarkup: options.ReplyMarkup,
				duration: options.Duration,
				width: options.Width,
				height: options.Height,
				thumbnail: options.Thumbnail,
				messageThreadId: options.MessageThreadId,
				showCaptionAboveMedia: options.ShowCaptionAboveMedia,
				hasSpoiler: options.HasSpoiler,
				supportsStreaming: options.SupportsStreaming,
				disableNotification: options.DisableNotification,
				protectContent: options.ProtectContent,
				messageEffectId: options.MessageEffectId,
				businessConnectionId: options.BusinessConnectionId,
				allowPaidBroadcast: options.AllowPaidBroadcast,
				cancellationToken: ct
			);
		}
		catch (Exception ex)
		{
			return new ExceptionError(ex, "Failed to send video.");
		}
	}

	/// <summary>
	/// Send an audio contextually.
	/// </summary>
	/// <param name="audio">The audio to send.</param>
	/// <param name="caption">The caption for the audio.</param>
	/// <param name="parseMode">The parse mode for the caption.</param>
	/// <param name="options">The message options to use.</param>
	/// <param name="ct">The cancellation token for this operation.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	public async Task<Result<Message>> SendContextualAudioAsync(InputFile audio, string? caption = null, ParseMode parseMode = default,
		FeedbackMessageOptions? options = null, CancellationToken ct = default)
	{
		var chatId = GetContextualChatId();
		if (!chatId.IsSuccess)
		{
			return Result<Message>.FromError(chatId.Error);
		}

		try
		{
			if (options is null)
			{
				return await botClient.SendAudio(chatId.Entity, audio, caption: caption, parseMode: parseMode, cancellationToken: ct);
			}

			return await botClient.SendAudio(
				chatId: chatId.Entity,
				audio: audio,
				caption: caption,
				parseMode: parseMode,
				replyParameters: options.ReplyParameters,
				replyMarkup: options.ReplyMarkup,
				duration: options.Duration,
				performer: options.Performer,
				title: options.Title,
				thumbnail: options.Thumbnail,
				messageThreadId: options.MessageThreadId,
				disableNotification: options.DisableNotification,
				protectContent: options.ProtectContent,
				messageEffectId: options.MessageEffectId,
				businessConnectionId: options.BusinessConnectionId,
				allowPaidBroadcast: options.AllowPaidBroadcast,
				cancellationToken: ct
			);
		}
		catch (Exception ex)
		{
			return new ExceptionError(ex, "Failed to send audio.");
		}
	}

	private Result<ChatId> GetContextualChatId()
	{
		if (contextInjection.Context is null)
		{
			return new InvalidOperationError("Contextual sends require a context to be available.");
		}

		var chatId = contextInjection.Context switch
		{
			IMessageContext messageContext => messageContext.Message.Chat.Id,
			IInteractionContext interactionContext => interactionContext.Interaction.GetChatId(),
			_ => throw new InvalidOperationException($"Unknown context type: {contextInjection.Context.GetType().Name}"),
		};

		return Result<ChatId>.FromSuccess(chatId);
	}
}
