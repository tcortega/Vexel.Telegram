# Vexel.Telegram: v1 → v2 differences

v2 is a **breaking major**.
There are no compatibility shims.
If you are on the released NuGet line (`master`), stay there until v2 is published.
This page is for migrators reading the `v2` branch (or a future v2 package) and rewriting an app.

The living example is [`samples/Vexel.Telegram.Sample`](../samples/Vexel.Telegram.Sample).
Prefer that over this page when the two disagree: the sample compiles.

## At a glance

| Area | v1 | v2 |
| --- | --- | --- |
| Handler engine | Remora.Commands + reflection groups | [Immediate.Handlers](https://github.com/ImmediatePlatform/Immediate.Handlers) + Vexel source generators |
| App model | `CommandGroup` / `InteractionGroup` multi-method classes | One static `partial` handler type per route (dual attributes) |
| Results | `Remora.Results` `IResult` everywhere | Exceptions + normal return; no Remora |
| Feedback | `IFeedbackService` | Concrete `Feedback` |
| Contexts | `ITextCommandContext`, `IInteractionContext`, … | Concrete `MessageContext`, `CallbackContext`, `InlineQueryContext`, `ChosenInlineResultContext` |
| Multi-turn | `IConversationStateService` + `[TextResponse]` | Concrete `Flow` + `IFlowStore` + pure flow-step handlers |
| Responders | `IResponder<T>` bus | `[On*]` observers + `IRawUpdateHandler` escape hatch |
| DI entry | `AddTelegramService` + `AddTelegramCommands().AddCommandTree()…` | Three explicit calls (see below) |
| Webhook HTTP | Hosting-adjacent | Separate `Vexel.Telegram.AspNetCore` + explicit `MapTelegramWebhook` |
| Inline query routing | Broken (routed on Telegram’s opaque query id) | Routes on **query text** |
| Packages | Client, Commands, Interactivity, Abstractions, Extensions, Hosting, metapackage | Client, Handlers, Hosting, AspNetCore, metapackage (generator ships as an analyzer) |

The legacy v1 projects (`Abstractions`, `Commands`, `Interactivity`, `Extensions`) are **deleted from the `v2` branch**.
Read them on `master` if you need the old source while porting.

## Handler model

v1 put many commands on one `CommandGroup` and many interactions on one `InteractionGroup`, discovered at runtime.

v2 is dual-attribute and source-generated (Immediate.Apis parity):

1. `[Handler]` from Immediate.Handlers (required on every handler type).
2. Exactly one Vexel route or observer attribute, **except** pure flow steps (those are `[Handler]` alone).

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

Route attributes:

* `[Command("name")]`: Bot API command entity at offset 0
* `[Callback("key")]`: callback data up to the first `|`
* `[InlineQuery("trigger")]` or `[InlineQuery]`: first whitespace token of the query text; empty attribute is the default
* `[ChosenInlineResult("key")]`: `ResultId` prefix up to `|`
* `[OnMessage]` / `[OnCallbackQuery]` / `[OnInlineQuery]` / `[OnChosenInlineResult]`: observers (empty request only; inject a context for payload)

Handlers must be `partial`.
Request shape is a nested record bound from the update (empty, single `string`, or tokenized primitives for commands).
See the sample and the analyzer diagnostics (`VEX0001`…`VEX0007`) when a shape is wrong.

### What replaced what

| v1 | v2 |
| --- | --- |
| `[Command("ping")]` method on `CommandGroup` | `[Handler]` + `[Command("ping")]` type |
| `[CallbackButton("x")]` | `[Handler]` + `[Callback("x")]` |
| `[InlineQuery("search")]` | `[Handler]` + `[InlineQuery("search")]` (text routing) |
| `[ChosenInlineResult(...)]` | `[Handler]` + `[ChosenInlineResult(...)]` |
| `[TextResponse("step")]` + conversation state | `[Handler]`-only flow step + `Flow.PromptAsync<TRequest>()` |
| `IResponder<Update>` / typed responders | `[On*]` and/or `IRawUpdateHandler` |
| `InteractionIdHelper` path ids | Short callback keys (`key` / `key\|suffix`), 64-byte UTF-8 cap enforced |

## Packages and DI

### Package layout

* **`Vexel.Telegram.Client`**: receive (polling or webhook), per-chat `UpdateScheduler`, raw handler registry
* **`Vexel.Telegram.Handlers`**: attributes, contexts, `Feedback`, `Flow`/`IFlowStore`, router, keyboards
* **`Vexel.Telegram.Generators`**: Roslyn generator + analyzers (not published alone).
  The `Handlers` nupkg embeds it under `analyzers/dotnet/cs`, so NuGet consumers of Handlers get route generation and VEX diagnostics automatically **when they build with the .NET 10 SDK or newer**.
  The generator is built against Roslyn 4.14; on the .NET 8 or 9 SDK the compiler reports `CS9057` and skips it, so no `AddXxxTelegram()` is generated and no `VEX` diagnostic fires.
  Target frameworks are unaffected - a bot built on the .NET 10 SDK still runs on .NET 8.
  Monorepo `ProjectReference` consumers still need an explicit Generators analyzer reference - analyzer project references do not flow transitively.
* **`Vexel.Telegram.Hosting`**: `BackgroundService` receive loop + auto `SetMyCommands` (no ASP.NET Core framework reference)
* **`Vexel.Telegram.AspNetCore`**: sole `FrameworkReference` to ASP.NET Core; owns `MapTelegramWebhook`
* **`Vexel.Telegram`**: metapackage of Client + Handlers + Hosting (**excludes** AspNetCore)

Production apps should prefer explicit package references over the metapackage once packages publish.
Package consumers only need `Vexel.Telegram.Handlers` (plus Client/Hosting/AspNetCore as needed); they do not add a separate Generators package.

### Three-call wiring

v2 never hides Immediate or the generated route table inside one magic call:

```csharp
builder.Services.AddTelegramBot(_ => token);       // client, host, contexts, Feedback, Flow, router
builder.Services.AddXxxHandlers();                 // Immediate.Handlers DI (name from your assembly)
builder.Services.AddXxxTelegram();                 // Vexel-generated routes / flow steps / On*
```

For webhook bots, reference `Vexel.Telegram.AspNetCore` and map the endpoint yourself:

```csharp
app.MapTelegramWebhook(); // nothing is auto-mapped; secret_token is the auth
```

`IServiceCollection.AddTelegramService` is still the `Vexel.Telegram.Hosting` primitive (client + hosted receive loop + `SetMyCommands`).
`AddTelegramBot` calls it for you and adds contexts, `Feedback`, `Flow`, and the router, so app code should call `AddTelegramBot`.
There is no `IHostBuilder` overload.

Receive mode is exclusive: polling (default) **or** webhook, validated at host start.
`SetMyCommands` runs automatically from `[Command]` metadata; opt out with `VexelClientOptions.RegisterBotCommands = false`.

## Dispatch order

Per chat lane, every update runs:

1. **Routed** handler (command / callback / inline / chosen / armed flow step), if any
2. **`[On*]`** observers for that kind: always, sequential, ordered by handler fully-qualified metadata name, each fault-isolated
3. **`IRawUpdateHandler`** instances registered via `AddRawUpdateHandler<T>`: always last; cannot suppress routing.
   `AddRawUpdateHandler<T>` is the only raw registration path - a plain `IRawUpdateHandler` container registration is never dispatched

Step 1 is a single `IUpdateRouter` (`TelegramRouter`), not a composed chain: routes come from the generated
`Add{Assembly}Telegram()` contributions, and registering your own `IUpdateRouter` throws instead of silently
taking routing away from `TelegramRouter`.

Handler faults are isolated per update/handler.
They do not kill the chat lane or the receive loop.
Fatal receive/config errors still stop the host (no zombie process).

### Update kinds in 2.0

Routed + On* cover **message, callback query, inline query, chosen inline result** only.

These are **raw-only** in 2.0 (use `IRawUpdateHandler`): edited messages, channel posts, chat member updates, join requests, shipping/pre-checkout, polls, reactions, business updates, and anything else Telegram adds.
v1’s responder bus could see some of these via `IResponder<T>`; that path is gone.

## Contexts, Feedback, Flow

All three are **concrete** types injected from the per-update scope (decision: no interface-for-mocking soup).
`IFlowStore` and `ITelegramBotClient` stay abstract where swapping is real.

### Feedback

Thin helper over the current update:

* `ReplyAsync`, `EditAsync`, `SendWithKeyboardAsync`
* `AnswerCallbackAsync`, `AnswerInlineAsync`: first-wins idempotent; only latch after the Bot API call succeeds

`Edit` needs a bot-authored message (callback or chosen inline result).
Plain message updates throw if you call it.

### Answer obligations

After the full pipeline, v2 discharges Telegram’s answer requirements when the app did not:

* callback updates → empty `answerCallbackQuery` if nothing answered
* inline updates → empty `answerInlineQuery` (`cache_time: 0`) if nothing answered

This runs on matched handlers, throws, silent success, and unrouted updates alike.
App answers via `Feedback` still win (first successful send latches).

### Flow (replaces conversation state)

```csharp
// arm the next step (from a command, a callback, or an earlier step)
await flow.PromptAsync<CollectName.Command>(cancellationToken: token);

// inside the armed step handler (its request carries the user's text): read, mutate, write back
var draft = await flow.GetDraftAsync<SignupDraft>(token) ?? new SignupDraft();
draft.Name = command.Name.Trim();
await flow.SetDraftAsync(draft, token);
await flow.PromptAsync<CollectAge.Command>(cancellationToken: token);

await flow.CancelAsync(token); // or built-in /cancel
```

Important semantics:

* `PromptAsync` takes the **request** type, not the handler class (handler types are `static`).
* Step key is `typeof(TRequest).FullName`.
* Pure flow steps: `[Handler]` only, empty or single-string request; payload is `Message.Text ?? Caption ?? ""`.
* Success without re-arm **auto-completes**; an exception **keeps the step armed** and the router sends a short generic retry line.
* Default TTL is 15 minutes (lazy expiry on read).
* Draft must be JSON-serializable POCO; 2.0 ships `MemoryFlowStore` only.
* While a step is armed, **leading-`/` text is still command-routed first**.
  Built-in `/cancel` works when the app has flow steps, defines no `[Command("cancel")]`, and something is armed.
  An unknown `/foo` is a command miss: it is **not** free text for the step.

v1’s `TryGetAwaitingInput` consumed state on read before the handler ran, so a parse failure could strand the user in silence.
v2 keeps the step armed until success-without-rearm, cancel, or TTL.

## Callback and keyboard data

Use `InlineKeyboardBuilder` and `CallbackData.Format` from `Vexel.Telegram.Handlers.Keyboards`.

* Callback route key = data up to first `|`; optional suffix binds as a single string
* The analyzer (`VEX0002`) checks the literal key on `[Callback]` / `[ChosenInlineResult]` at compile time
* The full `key|suffix` payload is composed at runtime, so the Bot API 64-byte UTF-8 cap is enforced there: `CallbackData.Format` / `CallbackData.EnsureWithinLimit` throw, and `CallbackData.IsWithinLimit` checks without throwing

Do not build Remora-style interaction tree paths.

## Testing status (honest)

| Suite | Status on v2 |
| --- | --- |
| `tests/Vexel.Telegram.Tests` | Unit + generator snapshots + in-process host E2E against a **fake** Bot API (always CI) |
| Real Telegram **test-DC** E2E (planned as `tests/Vexel.Telegram.E2E`, WTelegram user client, shared secrets) | **Does not exist yet on this branch.** Planned as its own suite outside the default CI path; needs provisioned secrets. Do not assume a green unit suite means a real-user pass. |

The sample bot is the fixture both the in-process host tests and the future test-DC suite drive.

## Migration checklist

1. Target the v2 package set (or project-reference the `v2` branch). Drop Remora package references.
2. Replace `AddTelegramService` + `AddTelegramCommands().AddCommandTree()…` with the three-call pattern.
3. Split each command/interaction method into its own `[Handler]` + route type; nest a request record; inject `Feedback` / contexts / `Flow`.
4. Rewrite conversation flows onto `Flow.PromptAsync` + pure flow-step handlers; delete `IConversationStateService` usage.
5. Replace `IResponder<T>` side effects with `[On*]` (four kinds above) or `IRawUpdateHandler` for everything else.
6. Replace `IFeedbackService` / `Result` plumbing with `Feedback` and exceptions/normal control flow.
7. Move webhook mapping to `Vexel.Telegram.AspNetCore` and call `MapTelegramWebhook` explicitly.
8. Fix inline query handlers to think in **query text triggers**, not interaction ids.
9. Run the sample mentally against your bot: `/cancel` mid-flow, unknown `/foo` mid-flow, callback answer paths, empty inline query.
10. Keep a real test-DC pass on your radar; unit/host fakes are necessary but not sufficient.

## Further reading

* Sample bot: [`samples/Vexel.Telegram.Sample`](../samples/Vexel.Telegram.Sample)
* Design archaeology / decision log (not an API contract): [`docs/v2-context.md`](./v2-context.md)
