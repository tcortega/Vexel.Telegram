using Remora.Results;
using Telegram.Bot;
using Vexel.Telegram.Interactivity;

namespace Vexel.Telegram.Sample.Interactions;

public class PaymentInteractions(
	ITelegramBotClient botClient,
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
		_ = await botClient.EditMessageText
		(
			chatId: chatId,
			messageId: callbackQuery.Message.MessageId,
			text: "Please enter the amount you wish to pay:",
			cancellationToken: CancellationToken
		);

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
		// We now have direct access to the message that triggered this handler.
		var message = context.Interaction.AsT3;

		_ = await botClient.SendMessage
		(
			chatId: message.Chat.Id,
			text: $"Thank you! Payment process for ${input:F2} started.",
			cancellationToken: CancellationToken
		);

		return Result.FromSuccess();
	}
}
