using Remora.Results;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Vexel.Telegram.Commands.Contexts;
using Vexel.Telegram.Commands.Extensions;

namespace Vexel.Telegram.Commands.Feedback;

/// <inheritdoc />
public class FeedbackService(ContextInjectionService contextInjection, ITelegramBotClient botClient) : IFeedbackService
{
	/// <inheritdoc />
	public async Task<Result<Message>> SendContextualMessageAsync(string message, ParseMode parseMode = default,
		FeedbackMessageOptions? options = null, CancellationToken ct = default)
	{
		if (contextInjection.Context is null)
		{
			return new InvalidOperationError("Contextual sends require a context to be available.");
		}

		var chatId = contextInjection.Context switch
		{
			IMessageContext messageContext => messageContext.Message.Chat.Id,
			IInteractionContext interactionContext => interactionContext.Interaction.GetChatId(),
			_ => throw new InvalidOperationException(),
		};

		if (options is null)
		{
			return await botClient.SendMessage(chatId, message, parseMode, cancellationToken: ct);
		}

		return await botClient.SendMessage(chatId, message, parseMode, options.ReplyParameters, options.ReplyMarkup, options.LinkPreviewOptions,
			disableNotification: options.DisableNotification, protectContent: options.ProtectContent, cancellationToken: ct);
	}
}
