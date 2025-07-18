using Remora.Results;
using Telegram.Bot;
using Vexel.Telegram.Commands;
using Vexel.Telegram.Interactivity;

namespace Vexel.Telegram.Sample.Interactions;

public class PaymentInteractions(
	ITelegramBotClient botClient,
	IFeedbackService feedbackService,
	IInteractionCommandContext context,
	IConversationStateService conversationState)
	: InteractionGroup
{
	[CallbackButton(nameof(StartPaymentAsync))]
	public async Task<IResult> StartPaymentAsync()
	{
		var callbackQuery = context.Interaction.AsT0;
		if (callbackQuery.Message is null)
		{
			await botClient.AnswerCallbackQuery
			(
				callbackQuery.Id, "Error: Cannot find message context.",
				showAlert: true,
				cancellationToken: CancellationToken
			);

			return Result.FromError(new NotFoundError("The callback query did not have an associated message."));
		}

		var chatId = callbackQuery.Message.Chat.Id;
		var result = await feedbackService.EditContextualMessageAsync
		(
			callbackQuery.Message.MessageId,
			"Please enter the amount you wish to pay:",
			ct: CancellationToken
		);

		if (!result.IsSuccess)
			return result;

		conversationState.SetAwaitingInput
		(
			chatId,
			context.User.Id,
			nameof(ReceivePaymentAmountAsync)
		);

		await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: CancellationToken);
		return Result.FromSuccess();
	}

	[TextResponse(nameof(ReceivePaymentAmountAsync))]
	public async Task<IResult> ReceivePaymentAmountAsync(decimal input)
	{
		return await feedbackService.SendContextualSuccessAsync
		(
			$"Thank you! Payment process for ${input:F2} started.",
			ct: CancellationToken
		);
	}
}
