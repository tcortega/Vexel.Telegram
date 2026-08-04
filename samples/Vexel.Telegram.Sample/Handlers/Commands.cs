using Immediate.Handlers.Shared;
using Telegram.Bot.Types.Enums;
using Vexel.Telegram.Handlers;
using Vexel.Telegram.Handlers.Attributes;
using Vexel.Telegram.Handlers.Contexts;
using Vexel.Telegram.Handlers.Keyboards;

namespace Vexel.Telegram.Sample.Handlers;

[Handler]
[Command("ping", Description = "Check that the bot is alive")]
public static partial class Ping
{
	public sealed record Command;

	private static async ValueTask HandleAsync(Command command, Feedback feedback, CancellationToken token)
	{
		_ = await feedback.ReplyAsync("Pong!", cancellationToken: token);
	}
}

[Handler]
[Command("echo", Description = "Echo the rest of the message")]
public static partial class Echo
{
	public sealed record Command(string Text);

	private static async ValueTask HandleAsync(Command command, Feedback feedback, CancellationToken token)
	{
		var body = string.IsNullOrWhiteSpace(command.Text) ? "(nothing to echo)" : command.Text;
		_ = await feedback.ReplyAsync($"You said: {body}", cancellationToken: token);
	}
}

[Handler]
[Command("help", Description = "Explain the sample surfaces")]
public static partial class Help
{
	public sealed record Command;

	private static async ValueTask HandleAsync(Command command, Feedback feedback, CancellationToken token)
	{
		_ = await feedback.ReplyAsync(HelpText.Body, parseMode: ParseMode.Markdown, cancellationToken: token);
	}
}

[Handler]
[Command("inline", Description = "Show how to try inline mode")]
public static partial class InlineTip
{
	public sealed record Command;

	private static async ValueTask HandleAsync(Command command, Feedback feedback, CancellationToken token)
	{
		var keyboard = new InlineKeyboardBuilder()
			.AddRow()
			.AddSwitchInlineQueryCurrentChatButton("Search here", "search kittens")
			.AddRow()
			.AddSwitchInlineQueryButton("Search anywhere", "search kittens")
			.Build();

		_ = await feedback.SendWithKeyboardAsync(
			"""
			*Inline mode*

			Type `@your_bot search kittens` in any chat, or tap a button below.

			Empty / unmatched queries hit the default `[InlineQuery]` handler.
			Picking a result routes `[ChosenInlineResult("item")]`.
			""",
			keyboard,
			parseMode: ParseMode.Markdown,
			cancellationToken: token);
	}
}

[Handler]
[Callback("ping")]
public static partial class PingCallback
{
	public sealed record Command;

	private static async ValueTask HandleAsync(Command command, Feedback feedback, CancellationToken token)
	{
		await feedback.AnswerCallbackAsync("Pong!", showAlert: true, cancellationToken: token);
		await feedback.EditAsync(
			$"Pong from a `[Callback]` handler at {DateTimeOffset.UtcNow:HH:mm:ss} UTC.",
			replyMarkup: MenuKeyboard.Build(),
			cancellationToken: token);
	}
}

[Handler]
[Callback("help")]
public static partial class HelpCallback
{
	public sealed record Command;

	private static async ValueTask HandleAsync(Command command, Feedback feedback, CancellationToken token)
	{
		await feedback.AnswerCallbackAsync(cancellationToken: token);
		await feedback.EditAsync(
			HelpText.Body,
			parseMode: ParseMode.Markdown,
			replyMarkup: new InlineKeyboardBuilder()
				.AddRow()
				.AddCallbackButton("Back to menu", "menu")
				.Build(),
			cancellationToken: token);
	}
}

[Handler]
[Callback("colors")]
public static partial class ColorsCallback
{
	public sealed record Command;

	private static async ValueTask HandleAsync(Command command, Feedback feedback, CancellationToken token)
	{
		await feedback.AnswerCallbackAsync(cancellationToken: token);

		var keyboard = new InlineKeyboardBuilder()
			.AddRow()
			.AddCallbackButton("Red", "pick_color", "red")
			.AddCallbackButton("Green", "pick_color", "green")
			.AddCallbackButton("Blue", "pick_color", "blue")
			.AddRow()
			.AddCallbackButton("Back", "menu")
			.Build();

		await feedback.EditAsync("Pick a color. The suffix after `|` binds to the handler.", replyMarkup: keyboard, cancellationToken: token);
	}
}

/// <summary>Suffix after the first <c>|</c> binds to the single string request parameter.</summary>
[Handler]
[Callback("pick_color")]
public static partial class PickColor
{
	public sealed record Command(string Color);

	private static async ValueTask HandleAsync(
		Command command,
		Feedback feedback,
		CallbackContext context,
		CancellationToken token)
	{
		await feedback.AnswerCallbackAsync($"Selected {command.Color}", cancellationToken: token);
		await feedback.EditAsync(
			$"{MarkdownText.Escape(context.From.FirstName)} picked *{MarkdownText.Escape(command.Color)}*.",
			parseMode: ParseMode.Markdown,
			replyMarkup: new InlineKeyboardBuilder()
				.AddRow()
				.AddCallbackButton("Pick again", "colors")
				.AddCallbackButton("Menu", "menu")
				.Build(),
			cancellationToken: token);
	}
}

internal static class HelpText
{
	internal const string Body =
		"""
		*What this sample shows*

		`[Handler]` + a Vexel route or `[On*]` attribute on every type (Immediate.Apis parity).

		• `/start`, `/menu` - button-first home keyboard
		• `/ping`, `/echo hi`, `/help`, `/inline`, `/signup`
		• Callbacks: `ping`, `signup`, `colors`, `pick_color|red`, `menu`, `help`
		• Flow: `/signup` or the Sign up button → name → age (`Flow.PromptAsync`)
		• Inline: `@bot search cats` and bare `@bot ...`
		• `[OnMessage]` observer logs every message after the routed leg

		Built-in `/cancel` clears an armed flow (unless you define your own `[Command("cancel")]`).

		Wiring in `Program.cs`:
		`AddTelegramBot` + `AddVexelTelegramSampleHandlers` + `AddVexelTelegramSampleTelegram`.
		""";
}
