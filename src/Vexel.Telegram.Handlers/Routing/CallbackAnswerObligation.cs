using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Vexel.Telegram.Client.Dispatch;
using FeedbackService = Vexel.Telegram.Handlers.Feedback;

namespace Vexel.Telegram.Handlers.Routing;

/// <summary>
/// B4 fail-closed answer obligation for callback queries: after every dispatch stage
/// (routed handler, exception, unrouted, and raw handlers), if <see cref="FeedbackService"/>
/// has not answered, send a default empty <c>answerCallbackQuery</c> so the client spinner
/// never hangs. Idempotent with app-level answers via Feedback's first-wins latch.
/// </summary>
public sealed class CallbackAnswerObligation(
	ITelegramBotClient botClient,
	ILogger<CallbackAnswerObligation> logger) : IUpdateCompletionHook
{
	/// <inheritdoc />
	public async Task CompleteAsync(
		Update update,
		IServiceProvider scope,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(update);
		ArgumentNullException.ThrowIfNull(scope);

		if (update.CallbackQuery is not { } callbackQuery)
		{
			return;
		}

		var feedback = scope.GetService<FeedbackService>();
		if (feedback is not null)
		{
			if (feedback.CallbackAnswered)
			{
				return;
			}

			try
			{
				await feedback.AnswerCallbackAsync(cancellationToken: cancellationToken)
					.ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				logger.LogWarning(
					ex,
					"Default answerCallbackQuery failed for callback {CallbackId} (update {UpdateId})",
					callbackQuery.Id,
					update.Id);
			}

			return;
		}

		// Hosts that wire the dispatcher without Feedback still must not leave the spinner.
		try
		{
			await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
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
}
