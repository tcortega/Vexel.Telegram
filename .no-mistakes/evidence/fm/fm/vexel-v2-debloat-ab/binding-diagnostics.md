# Binding-convention diagnostics

What a bot author sees when the dual-attr / binding rules are broken.

## The (broken) bot source

```csharp
using System.Threading;
using System.Threading.Tasks;
using Immediate.Handlers.Shared;
using Vexel.Telegram.Handlers.Attributes;

namespace DemoBot;

// VEX0001: [Command] without the sibling [Handler].
[Command("ping")]
public static class Ping
{
	public sealed record Command;
}

// VEX0003: not a legal Telegram command name.
[Handler]
[Command("Bad Name")]
public static class Shout
{
	public sealed record Command;

	private static ValueTask HandleAsync(Command command, CancellationToken token) => default;
}

// VEX0004: the request shape cannot be bound from command text.
[Handler]
[Command("tags")]
public static class Tags
{
	public sealed record Command(string[] Items);

	private static ValueTask HandleAsync(Command command, CancellationToken token) => default;
}

// VEX0005: two handlers claim the same command key.
[Handler]
[Command("status")]
public static class StatusA
{
	public sealed record Command;

	private static ValueTask HandleAsync(Command command, CancellationToken token) => default;
}

[Handler]
[Command("status")]
public static class StatusB
{
	public sealed record Command;

	private static ValueTask HandleAsync(Command command, CancellationToken token) => default;
}
```

## Build output (line: severity id: message)

```text
10: error VEX0001: Handler 'Ping' must be marked with [Handler] so Immediate.Handlers generates X.Handler
17: error VEX0003: Command name 'Bad Name' is invalid; Telegram requires 1-32 characters of lowercase a-z, digits, and underscores
28: error VEX0004: Request parameter 'Items' of type 'string[]' on 'Tags' is not bindable. Command request params must be string, int, long, bool, decimal, or enum (binding convention rule 6).
38: error VEX0005: Duplicate command route key 'status' on 'DemoBot.StatusB' and 'DemoBot.StatusA'
46: error VEX0005: Duplicate command route key 'status' on 'DemoBot.StatusA' and 'DemoBot.StatusB'
```
