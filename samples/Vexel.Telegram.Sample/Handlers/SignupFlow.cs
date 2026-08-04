using System.Globalization;
using Immediate.Handlers.Shared;
using Vexel.Telegram.Handlers;
using Vexel.Telegram.Handlers.Attributes;

namespace Vexel.Telegram.Sample.Handlers;

/// <summary>JSON-serializable draft carried between flow steps (IFlowStore contract).</summary>
public sealed class SignupDraft
{
	public string Name { get; set; } = "";

	public int Age { get; set; }
}

/// <summary>Command entry into the signup conversation.</summary>
[Handler]
[Command("signup", Description = "Two-step registration flow demo")]
public static partial class Signup
{
	public sealed record Command;

	private static ValueTask HandleAsync(Command command, Flow flow, Feedback feedback, CancellationToken token) =>
		SignupStart.BeginAsync(flow, feedback, token);
}

/// <summary>Same flow started from the menu button.</summary>
[Handler]
[Callback("signup")]
public static partial class SignupCallback
{
	public sealed record Command;

	private static async ValueTask HandleAsync(Command command, Flow flow, Feedback feedback, CancellationToken token)
	{
		await feedback.AnswerCallbackAsync(cancellationToken: token);
		await feedback.EditAsync("Starting signup…", cancellationToken: token);
		await SignupStart.BeginAsync(flow, feedback, token);
	}
}

internal static class SignupStart
{
	internal static async ValueTask BeginAsync(Flow flow, Feedback feedback, CancellationToken token)
	{
		// Arm by request type - Immediate handler classes are static, so TNext is the nested record.
		await flow.PromptAsync<CollectName.Command>(cancellationToken: token);
		_ = await feedback.ReplyAsync(
			"What's your name?\n\n(Commands still win mid-flow: try /cancel. An unknown /foo is not free text.)",
			cancellationToken: token);
	}
}

/// <summary>
/// Pure flow step: <c>[Handler]</c> only, no route/On* attribute. Next text message binds here.
/// </summary>
[Handler]
public static partial class CollectName
{
	public sealed record Command(string Name);

	private static async ValueTask HandleAsync(
		Command command,
		Flow flow,
		Feedback feedback,
		CancellationToken token)
	{
		var name = command.Name.Trim();
		if (name.Length is 0)
		{
			// Throwing keeps the step armed so the user can retry (B3).
			throw new InvalidOperationException("name was empty");
		}

		await flow.SetDraftAsync(new SignupDraft { Name = name }, token);
		await flow.PromptAsync<CollectAge.Command>(cancellationToken: token);
		_ = await feedback.ReplyAsync(
			$"Nice to meet you, {name}. How old are you? (send a number)",
			cancellationToken: token);
	}
}

[Handler]
public static partial class CollectAge
{
	public sealed record Command(string Age);

	private static async ValueTask HandleAsync(
		Command command,
		Flow flow,
		Feedback feedback,
		CancellationToken token)
	{
		if (!int.TryParse(command.Age.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var age)
			|| age is < 0 or > 130)
		{
			// B3: exception keeps the step armed + router sends a short generic retry line.
			throw new FormatException("age was not a sensible number");
		}

		var draft = await flow.GetDraftAsync<SignupDraft>(token) ?? new SignupDraft();
		draft.Age = age;

		// No re-arm and no explicit complete: successful return auto-completes the flow.
		_ = await feedback.ReplyAsync(
			$"Registered {draft.Name}, age {age}. Flow complete.\n\n/menu to go home.",
			cancellationToken: token);
	}
}
