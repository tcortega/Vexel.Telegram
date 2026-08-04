using Immediate.Handlers.Shared;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Vexel.Telegram.Handlers;
using Vexel.Telegram.Handlers.Attributes;
using Vexel.Telegram.Handlers.Keyboards;

namespace Vexel.Telegram.Sample.Handlers;

/// <summary>
/// Button-first home surface. <c>/start</c> and <c>/menu</c> both land here so deep links and
/// the command menu open the same keyboard.
/// </summary>
[Handler]
[Command("start", Description = "Open the sample menu")]
public static partial class Start
{
	public sealed record Command;

	private static ValueTask HandleAsync(Command command, Feedback feedback, CancellationToken token) =>
		MenuKeyboard.SendAsync(feedback, token);
}

[Handler]
[Command("menu", Description = "Open the sample menu")]
public static partial class Menu
{
	public sealed record Command;

	private static ValueTask HandleAsync(Command command, Feedback feedback, CancellationToken token) =>
		MenuKeyboard.SendAsync(feedback, token);
}

/// <summary>Same keyboard reachable from a callback so buttons can "go home" without a new command.</summary>
[Handler]
[Callback("menu")]
public static partial class MenuCallback
{
	public sealed record Command;

	private static async ValueTask HandleAsync(Command command, Feedback feedback, CancellationToken token)
	{
		await feedback.AnswerCallbackAsync(cancellationToken: token);
		await feedback.EditAsync(
			MenuKeyboard.Text,
			parseMode: ParseMode.Markdown,
			replyMarkup: MenuKeyboard.Build(),
			cancellationToken: token);
	}
}

internal static class MenuKeyboard
{
	internal const string Text =
		"""
		*Vexel.Telegram v2 sample*

		Button-first menu. Tap a row, or use a slash command.

		• *Ping* - callback route
		• *Sign up* - multi-turn `Flow.PromptAsync`
		• *Colors* - callback with a bound suffix
		• *Inline tip* - try `@bot search cats`
		• *Help* - what each piece demonstrates

		Commands always win over an armed flow, so `/menu` and `/cancel` work mid-signup.
		An unknown `/foo` mid-flow is a command miss (falls through to `On*`) - not free text.
		""";

	internal static InlineKeyboardMarkup Build() =>
		new InlineKeyboardBuilder()
			.AddRow()
			.AddCallbackButton("Ping", "ping")
			.AddCallbackButton("Sign up", "signup")
			.AddRow()
			.AddCallbackButton("Colors", "colors")
			.AddCallbackButton("Help", "help")
			.AddRow()
			.AddSwitchInlineQueryCurrentChatButton("Try inline here", "search ")
			.AddSwitchInlineQueryButton("Try inline anywhere", "search ")
			.Build();

	internal static async ValueTask SendAsync(Feedback feedback, CancellationToken token)
	{
		_ = await feedback.SendWithKeyboardAsync(
			Text,
			Build(),
			parseMode: ParseMode.Markdown,
			cancellationToken: token);
	}
}
