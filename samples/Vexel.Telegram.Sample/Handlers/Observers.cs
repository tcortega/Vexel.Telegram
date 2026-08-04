using Immediate.Handlers.Shared;
using Microsoft.Extensions.Logging;
using Vexel.Telegram.Handlers.Attributes;
using Vexel.Telegram.Handlers.Contexts;

namespace Vexel.Telegram.Sample.Handlers;

/// <summary>
/// On* observers always run *after* the routed handler (command / callback / flow / inline),
/// sequentially in fully-qualified metadata name order, on the same chat lane.
/// They observe; they do not replace routing. This one only logs so the chat stays quiet.
/// </summary>
[Handler]
[OnMessage]
public static partial class MessageAudit
{
	public sealed record Command;

	private static ValueTask HandleAsync(
		Command command,
		MessageContext context,
		ILogger<Command> logger,
		CancellationToken token)
	{
		logger.LogInformation(
			"OnMessage after routed leg: chat={ChatId} user={UserId} text={Text}",
			context.ChatId,
			context.UserId,
			context.Message.Text ?? context.Message.Caption ?? "(non-text)");
		return default;
	}
}

[Handler]
[OnCallbackQuery]
public static partial class CallbackAudit
{
	public sealed record Command;

	private static ValueTask HandleAsync(
		Command command,
		CallbackContext context,
		ILogger<Command> logger,
		CancellationToken token)
	{
		logger.LogInformation(
			"OnCallbackQuery after routed leg: data={Data} from={UserId}",
			context.Data,
			context.From.Id);
		return default;
	}
}

[Handler]
[OnInlineQuery]
public static partial class InlineAudit
{
	public sealed record Command;

	private static ValueTask HandleAsync(
		Command command,
		InlineQueryContext context,
		ILogger<Command> logger,
		CancellationToken token)
	{
		logger.LogInformation(
			"OnInlineQuery after routed leg: query={Query} from={UserId}",
			context.Query,
			context.UserId);
		return default;
	}
}
