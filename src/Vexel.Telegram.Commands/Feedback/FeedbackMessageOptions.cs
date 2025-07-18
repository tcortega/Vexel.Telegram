using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Vexel.Telegram.Commands;

/// <summary>
/// Options for customizing feedback messages.
/// </summary>
public record FeedbackMessageOptions
{
	/// <summary>
	/// Description of the message to reply to.
	/// </summary>
	public ReplyParameters? ReplyParameters { get; init; }

	/// <summary>
	/// Additional interface options. A keyboard, instructions to remove keyboard or to force a reply from the user.
	/// </summary>
	public ReplyMarkup? ReplyMarkup { get; init; }

	/// <summary>
	/// Link preview generation options for the message.
	/// </summary>
	public LinkPreviewOptions? LinkPreviewOptions { get; init; }

	/// <summary>
	/// Protects the contents of the sent message from forwarding and saving.
	/// </summary>
	public bool ProtectContent { get; init; }

	/// <summary>
	/// Sends the message silently. Users will receive a notification with no sound.
	/// </summary>
	public bool DisableNotification { get; init; }

	/// <summary>
	/// Unique identifier for the target message thread (topic) of the forum; for forum supergroups only.
	/// </summary>
	public int? MessageThreadId { get; init; }

	/// <summary>
	/// Unique identifier of the message effect to be added to the message; for private chats only.
	/// </summary>
	public string? MessageEffectId { get; init; }

	/// <summary>
	/// Unique identifier of the business connection on behalf of which the message will be sent.
	/// </summary>
	public string? BusinessConnectionId { get; init; }

	/// <summary>
	/// Pass True to allow up to 1000 messages per second, ignoring broadcasting limits for a fee of 0.1 Telegram Stars per message.
	/// </summary>
	public bool AllowPaidBroadcast { get; init; }

	/// <summary>
	/// Pass True, if the caption must be shown above the media.
	/// </summary>
	public bool ShowCaptionAboveMedia { get; init; }

	/// <summary>
	/// Pass True if the photo needs to be covered with a spoiler animation.
	/// </summary>
	public bool HasSpoiler { get; init; }

	/// <summary>
	/// Thumbnail of the file sent; can be ignored if thumbnail generation for the file is supported server-side.
	/// </summary>
	public InputFile? Thumbnail { get; init; }

	/// <summary>
	/// Video width for video messages.
	/// </summary>
	public int? Width { get; init; }

	/// <summary>
	/// Video height for video messages.
	/// </summary>
	public int? Height { get; init; }

	/// <summary>
	/// Duration of the video/audio/voice in seconds.
	/// </summary>
	public int? Duration { get; init; }

	/// <summary>
	/// Performer of the audio.
	/// </summary>
	public string? Performer { get; init; }

	/// <summary>
	/// Track name of the audio.
	/// </summary>
	public string? Title { get; init; }

	/// <summary>
	/// Disables automatic server-side content type detection for files uploaded using multipart/form-data.
	/// </summary>
	public bool DisableContentTypeDetection { get; init; }

	/// <summary>
	/// Pass True if the video is suitable for streaming.
	/// </summary>
	public bool SupportsStreaming { get; init; }
}
