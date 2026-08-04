# The 64-byte callback_data limit

Telegram caps `callback_data` at 64 UTF-8 bytes. Vexel enforces it twice: VEX0002 at build
time on `[Callback]` keys, and the keyboard helpers at runtime on the data they emit.

## The (broken) bot source

```csharp
using System.Threading;
using System.Threading.Tasks;
using Immediate.Handlers.Shared;
using Vexel.Telegram.Handlers.Attributes;

namespace DemoBot;

// Fine: short key, well under the Telegram limit.
[Handler]
[Callback("confirm")]
public static partial class Confirm
{
	public sealed record Command(string OrderId);

	private static ValueTask HandleAsync(Command command, CancellationToken token) => default;
}

// VEX0002: 65 ASCII bytes of callback_data before any suffix is even added.
[Handler]
[Callback("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
public static partial class TooLong
{
	public sealed record Command;

	private static ValueTask HandleAsync(Command command, CancellationToken token) => default;
}

// VEX0002: 22 euro signs are 66 UTF-8 bytes, though only 22 characters.
[Handler]
[Callback("€€€€€€€€€€€€€€€€€€€€€€")]
public static partial class TooManyBytes
{
	public sealed record Command;

	private static ValueTask HandleAsync(Command command, CancellationToken token) => default;
}

// VEX0002: '|' is the key/suffix separator, so it cannot appear in the key.
[Handler]
[Callback("bad|key")]
public static partial class PipeInKey
{
	public sealed record Command;

	private static ValueTask HandleAsync(Command command, CancellationToken token) => default;
}

// VEX0002: blank keys would send zero bytes of callback_data.
[Handler]
[Callback("   ")]
public static partial class Blank
{
	public sealed record Command;

	private static ValueTask HandleAsync(Command command, CancellationToken token) => default;
}
```

## Build output (line: severity id: message)

```text
20: error VEX0002: Callback route key is 65 UTF-8 bytes; Telegram allows at most 64 for callback_data
30: error VEX0002: Callback route key is 66 UTF-8 bytes; Telegram allows at most 64 for callback_data
40: error VEX0002: Callback route key 'bad|key' must not contain '|' (the key/suffix separator)
50: error VEX0002: Callback route key must not be empty or whitespace; Telegram requires 1-64 UTF-8 bytes of callback_data
```

## Callback routes the generator emitted for that source

Only the valid key reaches the route table; the rejected keys are never registered.

```csharp
		var callbacks = new global::System.Collections.Generic.Dictionary<string, global::Vexel.Telegram.Handlers.Routing.RouteBinder>(
			global::System.StringComparer.Ordinal)
		{
			["confirm"] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::DemoBot.Confirm.Handler>(scope);
				_ = await handler.HandleAsync(new global::DemoBot.Confirm.Command(payload), cancellationToken).ConfigureAwait(false);
				return true;
			},
		};
```

## Runtime: what the keyboard helpers do with oversized data

```text
suffix pushes data over the limit: ArgumentException: Callback data is 78 UTF-8 bytes; Telegram allows at most 64.
64 bytes counted as UTF-8, not characters: ArgumentException: Callback data is 66 UTF-8 bytes; Telegram allows at most 64.
'|' in the route key: ArgumentException: Callback route key must not contain '|'.
blank route key: ArgumentException: Callback route key must not be empty or whitespace; Telegram requires 1-64 UTF-8 bytes of callback_data.
```
