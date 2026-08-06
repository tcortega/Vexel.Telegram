using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InlineQueryResults;
using Vexel.Telegram.Client.Dispatch;
using FeedbackService = Vexel.Telegram.Handlers.Feedback;

namespace Vexel.Telegram.Handlers.Routing;

/// <summary>
/// B4 fail-closed answer obligation for callback and inline queries: after every dispatch stage
/// (routed handler, exception, unrouted, and raw handlers), if <see cref="FeedbackService"/>
/// has not answered, send a default empty <c>answerCallbackQuery</c> / empty
/// <c>answerInlineQuery</c> (<c>cache_time: 0</c>) so the client spinner never hangs.
/// Idempotent with app-level answers via Feedback's first-wins latch.
/// </summary>
/// <remarks>
/// The scoped <see cref="FeedbackService"/> is the only answer path this hook observes. Answering
/// through an injected <see cref="ITelegramBotClient"/> (or a self-constructed Feedback) leaves the
/// latch unset, so this hook still attempts its default answer; answer via
/// <see cref="FeedbackService.AnswerCallbackAsync"/> / <see cref="FeedbackService.AnswerInlineAsync"/>
/// even from raw handlers. A default answer that Telegram rejects because the query was already
/// answered or expired is treated as satisfied.
/// </remarks>
internal sealed class AnswerObligation(
	ITelegramBotClient botClient,
	ILogger<AnswerObligation> logger) : IUpdateCompletionHook
{
	private static readonly InlineQueryResult[] s_emptyResults = [];

	/// <inheritdoc />
	public Task CompleteAsync(
		Update update,
		IServiceProvider scope,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(update);
		ArgumentNullException.ThrowIfNull(scope);

		if (update.CallbackQuery is not null)
		{
			return CompleteCallbackAsync(update, scope, cancellationToken);
		}

		if (update.InlineQuery is not null)
		{
			return CompleteInlineAsync(update, scope, cancellationToken);
		}

		return Task.CompletedTask;
	}

	private async Task CompleteCallbackAsync(
		Update update,
		IServiceProvider scope,
		CancellationToken cancellationToken)
	{
		var callbackQuery = update.CallbackQuery!;
		var feedback = scope.GetService<FeedbackService>();
		if (feedback is { CallbackAnswered: true })
		{
			return;
		}

		try
		{
			// Hosts that wire the dispatcher without Feedback still must not leave the spinner.
			await (feedback is not null
				? feedback.AnswerCallbackAsync(cancellationToken: cancellationToken)
				: botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken))
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (ApiRequestException ex) when (IsAlreadyAnswered(ex))
		{
			logger.LogDebug(
				"Callback {CallbackId} (update {UpdateId}) was already answered outside Feedback; " +
				"default answerCallbackQuery skipped",
				callbackQuery.Id,
				update.Id);
		}
		catch (Exception ex)
		{
			logger.LogWarning(
				ex,
				"Default answerCallbackQuery failed for callback {CallbackId} (update {UpdateId})",
				callbackQuery.Id,
				update.Id);
		}
	}

	private async Task CompleteInlineAsync(
		Update update,
		IServiceProvider scope,
		CancellationToken cancellationToken)
	{
		var inlineQuery = update.InlineQuery!;
		var feedback = scope.GetService<FeedbackService>();
		if (feedback is { InlineAnswered: true })
		{
			return;
		}

		try
		{
			// Hosts that wire the dispatcher without Feedback still must not leave the client waiting.
			await (feedback is not null
				? feedback.AnswerInlineAsync(s_emptyResults, cacheTime: 0, cancellationToken: cancellationToken)
				: botClient.AnswerInlineQuery(
					inlineQuery.Id,
					s_emptyResults,
					cacheTime: 0,
					cancellationToken: cancellationToken))
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (ApiRequestException ex) when (IsAlreadyAnswered(ex))
		{
			logger.LogDebug(
				"Inline query {InlineQueryId} (update {UpdateId}) was already answered outside Feedback; " +
				"default answerInlineQuery skipped",
				inlineQuery.Id,
				update.Id);
		}
		catch (Exception ex)
		{
			logger.LogWarning(
				ex,
				"Default answerInlineQuery failed for inline query {InlineQueryId} (update {UpdateId})",
				inlineQuery.Id,
				update.Id);
		}
	}

	private static bool IsAlreadyAnswered(ApiRequestException exception) =>
		exception.ErrorCode == 400
		&& (exception.Message.Contains("query is too old", StringComparison.OrdinalIgnoreCase)
			|| exception.Message.Contains("query ID is invalid", StringComparison.OrdinalIgnoreCase)
			|| exception.Message.Contains("QUERY_ID_INVALID", StringComparison.OrdinalIgnoreCase));
}
