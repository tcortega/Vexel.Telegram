using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.ReplyMarkups;
using Vexel.Telegram.Handlers.Contexts;

namespace Vexel.Telegram.Handlers;

/// <summary>
/// Thin per-update feedback helper. Defaults (chat, message, callback/inline ids) come from the
/// scoped <see cref="UpdateContextHolder"/>. <see cref="AnswerCallbackAsync"/> and
/// <see cref="AnswerInlineAsync"/> are idempotent: the first call wins.
/// </summary>
public sealed class Feedback(ITelegramBotClient botClient, UpdateContextHolder holder)
{
	private const int NotAnswered = 0;
	private const int Answering = 1;
	private const int Answered = 2;

	private int _callbackState;
	private int _inlineState;

	/// <summary>True once <see cref="AnswerCallbackAsync"/> has answered the callback query.</summary>
	public bool CallbackAnswered => Volatile.Read(ref _callbackState) == Answered;

	/// <summary>True once <see cref="AnswerInlineAsync"/> has answered the inline query.</summary>
	public bool InlineAnswered => Volatile.Read(ref _inlineState) == Answered;

	/// <summary>
	/// Sends <paramref name="text"/> to the contextual chat.
	/// </summary>
	/// <param name="text">Message text.</param>
	/// <param name="parseMode">Optional parse mode.</param>
	/// <param name="replyMarkup">Optional keyboard markup.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The sent message.</returns>
	public Task<Message> ReplyAsync(
		string text,
		ParseMode parseMode = default,
		ReplyMarkup? replyMarkup = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(text);

		var chatId = RequireChatId();
		return botClient.SendMessage(
			chatId,
			text,
			parseMode: parseMode,
			replyMarkup: replyMarkup,
			cancellationToken: cancellationToken);
	}

	/// <summary>
	/// Edits the contextual bot-authored message text (callback chat message, callback inline
	/// message, or a chosen inline result's message). Plain message updates carry the user's own
	/// inbound message, which a bot cannot edit, so they have no editable context here: capture the
	/// <see cref="Message"/> returned by <see cref="ReplyAsync"/> and edit it through
	/// <see cref="ITelegramBotClient"/> instead.
	/// </summary>
	/// <param name="text">New message text.</param>
	/// <param name="parseMode">Optional parse mode.</param>
	/// <param name="replyMarkup">Optional inline keyboard.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	public async Task EditAsync(
		string text,
		ParseMode parseMode = default,
		InlineKeyboardMarkup? replyMarkup = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(text);

		if (holder.Callback is { } callback)
		{
			if (callback.Message is { } message)
			{
				_ = await botClient.EditMessageText(
					message.Chat.Id,
					message.Id,
					text,
					parseMode: parseMode,
					replyMarkup: replyMarkup,
					cancellationToken: cancellationToken).ConfigureAwait(false);
				return;
			}

			if (callback.InlineMessageId is { } inlineMessageId)
			{
				await botClient.EditMessageText(
					inlineMessageId,
					text,
					parseMode: parseMode,
					replyMarkup: replyMarkup,
					cancellationToken: cancellationToken).ConfigureAwait(false);
				return;
			}

			throw new InvalidOperationException(
				"Cannot edit: callback query has neither a chat message nor an inline message id.");
		}

		if (holder.ChosenInlineResult is { InlineMessageId: { } chosenInlineMessageId })
		{
			await botClient.EditMessageText(
				chosenInlineMessageId,
				text,
				parseMode: parseMode,
				replyMarkup: replyMarkup,
				cancellationToken: cancellationToken).ConfigureAwait(false);
			return;
		}

		throw new InvalidOperationException(
			"Cannot edit: the current update has no editable bot message. " +
			"Edit is available for callback queries and chosen inline results; a plain message " +
			"update carries the user's own message, which the bot cannot edit.");
	}

	/// <summary>
	/// Answers the contextual callback query. Idempotent: the first call that reaches the Bot API
	/// wins; concurrent and later calls no-op. A failed send does not latch, so a caller that
	/// catches the exception can retry instead of leaving the client spinning.
	/// </summary>
	/// <param name="text">Optional notification/alert text.</param>
	/// <param name="showAlert">Whether to show an alert instead of a toast.</param>
	/// <param name="url">Optional URL to open.</param>
	/// <param name="cacheTime">Optional client-side cache time in seconds.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	public async Task AnswerCallbackAsync(
		string? text = null,
		bool showAlert = false,
		string? url = null,
		int? cacheTime = null,
		CancellationToken cancellationToken = default)
	{
		if (Interlocked.CompareExchange(ref _callbackState, Answering, NotAnswered) != NotAnswered)
		{
			return;
		}

		try
		{
			var callback = holder.Callback
				?? throw new InvalidOperationException(
					"AnswerCallback requires a CallbackQuery update context.");

			await botClient.AnswerCallbackQuery(
				callback.Id,
				text: text,
				showAlert: showAlert,
				url: url,
				cacheTime: cacheTime,
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			Volatile.Write(ref _callbackState, NotAnswered);
			throw;
		}

		Volatile.Write(ref _callbackState, Answered);
	}

	/// <summary>
	/// Answers the contextual inline query. Idempotent: the first call that reaches the Bot API
	/// wins; concurrent and later calls no-op. A failed send does not latch, so a caller that
	/// catches the exception can retry.
	/// </summary>
	/// <param name="results">Inline results to show.</param>
	/// <param name="cacheTime">Optional cache time in seconds.</param>
	/// <param name="isPersonal">Whether results are personal to the user.</param>
	/// <param name="nextOffset">Offset for pagination.</param>
	/// <param name="button">Optional results button above the results.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	public async Task AnswerInlineAsync(
		IEnumerable<InlineQueryResult> results,
		int? cacheTime = null,
		bool isPersonal = false,
		string? nextOffset = null,
		InlineQueryResultsButton? button = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(results);

		if (Interlocked.CompareExchange(ref _inlineState, Answering, NotAnswered) != NotAnswered)
		{
			return;
		}

		try
		{
			var inlineQuery = holder.InlineQuery
				?? throw new InvalidOperationException(
					"AnswerInline requires an InlineQuery update context.");

			await botClient.AnswerInlineQuery(
				inlineQuery.Id,
				results,
				cacheTime: cacheTime,
				isPersonal: isPersonal,
				nextOffset: nextOffset,
				button: button,
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			Volatile.Write(ref _inlineState, NotAnswered);
			throw;
		}

		Volatile.Write(ref _inlineState, Answered);
	}

	/// <summary>
	/// Sends <paramref name="text"/> to the contextual chat with <paramref name="replyMarkup"/>.
	/// </summary>
	/// <param name="text">Message text.</param>
	/// <param name="replyMarkup">Keyboard markup to attach.</param>
	/// <param name="parseMode">Optional parse mode.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The sent message.</returns>
	public Task<Message> SendWithKeyboardAsync(
		string text,
		ReplyMarkup replyMarkup,
		ParseMode parseMode = default,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(replyMarkup);

		var chatId = RequireChatId();
		return botClient.SendMessage(
			chatId,
			text,
			parseMode: parseMode,
			replyMarkup: replyMarkup,
			cancellationToken: cancellationToken);
	}

	private ChatId RequireChatId()
	{
		if (holder.Message is { } message)
		{
			return message.ChatId;
		}

		if (holder.Callback is { ChatId: { } callbackChatId })
		{
			return callbackChatId;
		}

		throw new InvalidOperationException(
			"Contextual send requires a chat id. " +
			"Inline-origin callbacks and inline queries have no chat; inject ITelegramBotClient for those cases.");
	}
}
