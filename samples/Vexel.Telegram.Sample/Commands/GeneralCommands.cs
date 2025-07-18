using System.ComponentModel;
using Vexel.Telegram.Commands;
using Vexel.Telegram.Extensions.Builders;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Results;
using Telegram.Bot.Types.Enums;
using Vexel.Telegram.Sample.Interactions;

namespace Vexel.Telegram.Sample.Commands;

/// <summary>
/// A command group containing general-purpose commands.
/// </summary>
public class GeneralCommands(IFeedbackService feedbackService, ITextCommandContext context) : CommandGroup
{
	[Command("ping"), Description("Checks if the bot is responsive.")]
	public async Task<IResult> PingAsync()
	{
		var options = new FeedbackMessageOptions { ReplyParameters = new() { MessageId = context.Message.MessageId }, };
		return await feedbackService.SendContextualMessageAsync("Pong!", options: options, ct: CancellationToken);
	}

	[Command("echo"), Description("Repeats the text you provide.")]
	public async Task<IResult> EchoAsync([Greedy] string text)
	{
		var options = new FeedbackMessageOptions { ReplyParameters = new() { MessageId = context.Message.MessageId }, };
		return await feedbackService.SendContextualMessageAsync($"You said: {text}", options: options, ct: CancellationToken);
	}

	[Command("demo"), Description("Shows the interactive demo with buttons and callbacks.")]
	public async Task<IResult> DemoAsync()
	{
		var keyboard = new InlineKeyboardBuilder()
			.AddRow()
			.AddCallbackButton("🏓 Ping", "ping")
			.AddRow()
			.AddCallbackButton("🎨 Colors", "color")
			.AddUrlButton("🌐 Telegram", "https://telegram.org")
			.Build();

		var options = new FeedbackMessageOptions { ReplyMarkup = keyboard };
		return await feedbackService.SendContextualMessageAsync(
			"🎮 **Interactive Demo Menu**\n\nChoose an option to test different interactions:",
			ParseMode.Markdown,
			options,
			CancellationToken
		);
	}

	[Command("inline"), Description("Shows how to use inline queries.")]
	public async Task<IResult> InlineAsync()
	{
		var keyboard = new InlineKeyboardBuilder()
			.AddRow()
			.AddSwitchInlineQueryCurrentChatButton("🔍 Try Inline Query Here", "hello")
			.AddRow()
			.AddSwitchInlineQueryButton("🔍 Try Inline Query Anywhere", "telegram")
			.Build();

		var options = new FeedbackMessageOptions { ReplyMarkup = keyboard };
		return await feedbackService.SendContextualMessageAsync(
			"🔍 **Inline Query Demo**\n\nClick the buttons below to try inline queries!\n\n" +
			"You can also try typing `@yourbotname query` in any chat to use inline mode.",
			ParseMode.Markdown,
			options,
			CancellationToken
		);
	}

	[Command("payment"), Description("Starts a sample payment workflow with the TextResponse usage")]
	public async Task<IResult> PaymentAsync()
	{
		var keyboard = new InlineKeyboardBuilder()
			.AddRow()
			.AddCallbackButton("💳 Start Payment", nameof(PaymentInteractions.StartPaymentAsync))
			.Build();

		var options = new FeedbackMessageOptions { ReplyMarkup = keyboard };
		return await feedbackService.SendContextualMessageAsync(
			"This sample payment workflow demonstrates how you can capture text inputs and route them to the proper handlers.",
			options: options,
			ct: CancellationToken
		);
	}
}
