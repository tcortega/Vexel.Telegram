using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Vexel.Telegram.Commands.Feedback;

public record FeedbackMessageOptions
{
	public ReplyParameters? ReplyParameters { get; init; }
	public ReplyMarkup? ReplyMarkup { get; init; }
	public LinkPreviewOptions? LinkPreviewOptions { get; init; }
	public bool ProtectContent { get; init; }
	public bool DisableNotification { get; init; }
}
