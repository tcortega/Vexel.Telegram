# `docs/v1-to-v2.md`: the guide's own snippet, run as a bot

The handler below was lifted verbatim out of the migration guide's Markdown, compiled with
the Immediate + Vexel generators, and registered through the guide's own three-call wiring
in a real host talking to a fake Telegram Bot API over HTTP.

## Snippet, exactly as `docs/v1-to-v2.md` prints it

```csharp
using Immediate.Handlers.Shared;
using Vexel.Telegram.Handlers;
using Vexel.Telegram.Handlers.Attributes;

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
```

## What Telegram saw

```text
user  >  /ping
  api <  setMyCommands commands=[/ping - "Check that the bot is alive"]
  api <  deleteWebhook drop_pending_updates=true
  api <  sendMessage chat_id=100 text="Pong!"
```

`setMyCommands` above is the guide's "`SetMyCommands` runs automatically from `[Command]`
metadata" claim, on the wire, from the snippet's own `Description`.
