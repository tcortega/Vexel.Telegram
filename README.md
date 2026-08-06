# Vexel.Telegram

[![NuGet](https://img.shields.io/nuget/v/Vexel.Telegram.svg?style=plastic)](https://www.nuget.org/packages/Vexel.Telegram/)
[![GitHub release](https://img.shields.io/github/release/tcortega/Vexel.Telegram.svg)](https://GitHub.com/tcortega/Vexel.Telegram/releases/)
[![GitHub license](https://img.shields.io/github/license/tcortega/Vexel.Telegram.svg)](https://github.com/tcortega/Vexel.Telegram/blob/master/LICENSE)
[![GitHub issues](https://img.shields.io/github/issues/tcortega/Vexel.Telegram.svg)](https://GitHub.com/tcortega/Vexel.Telegram/issues/)
[![GitHub issues-closed](https://img.shields.io/github/issues-closed/tcortega/Vexel.Telegram.svg)](https://GitHub.com/tcortega/Vexel.Telegram/issues?q=is%3Aissue+is%3Aclosed)
[![GitHub Actions](https://github.com/tcortega/Vexel.Telegram/actions/workflows/build.yml/badge.svg)](https://github.com/tcortega/Vexel.Telegram/actions)
---

> **Status:** this README documents **v2**, the rewrite living on this branch.
> v2 is a breaking major and is **not published to NuGet yet** - the packages on the feed are the v1 line from `master`.
> The v1 Remora-based projects (`Abstractions`, `Commands`, `Interactivity`, `Extensions`) are deleted here; their source and docs stay on `master`.
> Coming from v1? Read **[docs/v1-to-v2.md](./docs/v1-to-v2.md)**.

Vexel.Telegram is a C# library for building Telegram bots on top of
the [Telegram.Bot](https://github.com/TelegramBots/Telegram.Bot) library.
It is built to fulfill a need for robust, feature-complete, highly available and concurrent bots.

v2 is built on [Immediate.Handlers](https://github.com/ImmediatePlatform/Immediate.Handlers) plus Vexel source generators:
routing is a compile-time table, not a reflection scan, and handler shape mistakes are analyzer diagnostics instead of runtime surprises.

## Packages

| Package | Contents |
| --- | --- |
| `Vexel.Telegram.Client` | Receive (polling or webhook), per-chat ordered `UpdateScheduler`, dispatch, raw handlers |
| `Vexel.Telegram.Handlers` | Route attributes, contexts, `Feedback`, `Flow`/`IFlowStore`, router, keyboard builders |
| `Vexel.Telegram.Generators` | Roslyn generator + analyzers; not published alone - embedded in the Handlers nupkg under `analyzers/dotnet/cs` |
| `Vexel.Telegram.Hosting` | `BackgroundService` receive loop + automatic `SetMyCommands` (no ASP.NET Core dependency) |
| `Vexel.Telegram.AspNetCore` | Webhook ingress; owns `MapTelegramWebhook` |
| `Vexel.Telegram` | Metapackage of Client + Handlers + Hosting (**excludes** AspNetCore) |

Until v2 publishes, consume it by project reference from a checkout of this branch.
NuGet consumers of `Vexel.Telegram.Handlers` get the generator automatically from the package.
Monorepo `ProjectReference` consumers must still reference `Vexel.Telegram.Generators` themselves with `OutputItemType="Analyzer"`; analyzer project references do not flow transitively.

**Build with the .NET 10 SDK or newer.**
The libraries target `net8.0`-`net11.0`, so an app can still *run* on .NET 8, but the embedded generator is built against Roslyn 4.14.
Building with the .NET 8 or 9 SDK makes the compiler skip it (`CS9057`): no `AddXxxTelegram()` is generated and no `VEX` diagnostics run.

## Wiring a bot

Composition is three explicit calls - nothing about Immediate or the generated route table is hidden:

```csharp
using Vexel.Telegram.Handlers.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddTelegramBot(_ => "<BOT_TOKEN>"); // client, host, contexts, Feedback, Flow, router
builder.Services.AddMyBotHandlers();                 // Immediate.Handlers DI
builder.Services.AddMyBotTelegram();                 // Vexel-generated routes / flow steps / On*

await builder.Build().RunAsync();
```

The last two method names come from your assembly name (`My.Bot` → `AddMyBotHandlers` / `AddMyBotTelegram`).
Load the token from configuration in real apps.
Polling is the default receive mode; `AddTelegramBot` also takes optional `VexelClientOptions` and `FlowOptions` callbacks.

## Writing handlers

Every routed type carries two attributes: `[Handler]` from Immediate.Handlers, plus exactly one Vexel route or `[On*]` observer attribute.
Handler types are `static partial`; the request is a nested record bound from the update.

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

[Handler]
[Command("echo", Description = "Echo the rest of the message")]
public static partial class Echo
{
	public sealed record Command(string Text);

	private static async ValueTask HandleAsync(Command command, Feedback feedback, CancellationToken token)
	{
		_ = await feedback.ReplyAsync($"You said: {command.Text}", cancellationToken: token);
	}
}
```

Route attributes:

* `[Command("name")]` - Bot API command entity at offset 0; arguments bind from the request constructor
* `[Callback("key")]` - callback data up to the first `|`; the suffix binds as a single string
* `[InlineQuery("trigger")]` / `[InlineQuery]` - first whitespace token of the **query text**; the empty attribute is the default
* `[ChosenInlineResult("key")]` - `ResultId` prefix up to `|`
* `[OnMessage]` / `[OnCallbackQuery]` / `[OnInlineQuery]` / `[OnChosenInlineResult]` - observers that run after routing (empty request; inject a context for the payload)

Anything outside those four update kinds (edited messages, channel posts, chat members, payments, polls, …) is reachable through `IRawUpdateHandler`, registered with `AddRawUpdateHandler<THandler>()`.
`[Command]` metadata is pushed to Telegram via `SetMyCommands` at host start; opt out with `VexelClientOptions.RegisterBotCommands = false`.

Multi-turn conversations use `Flow`: a `[Handler]` type with no route or `[On*]` attribute is a flow step, armed by request type.

```csharp
await flow.PromptAsync<CollectName.Command>(cancellationToken: token); // next text message routes to CollectName
```

Inject `Feedback` for replies (`ReplyAsync`, `EditAsync`, `SendWithKeyboardAsync`, `AnswerCallbackAsync`, `AnswerInlineAsync`),
and `MessageContext` / `CallbackContext` / `InlineQueryContext` / `ChosenInlineResultContext` for the raw update.
Build callback payloads with `InlineKeyboardBuilder` and `CallbackData.Format` so the Bot API 64-byte cap is enforced for you.

## Webhooks

Webhook bots add `Vexel.Telegram.AspNetCore` and map the endpoint explicitly - nothing is auto-mapped:

```csharp
app.MapTelegramWebhook(); // secret_token is the auth
```

## Learn more

* **[samples/Vexel.Telegram.Sample](./samples/Vexel.Telegram.Sample)** - the living example: commands, callbacks, inline, flow, observers ([sample README](./samples/Vexel.Telegram.Sample/README.md))
* **[docs/v1-to-v2.md](./docs/v1-to-v2.md)** - what changed from v1 and how to port an app
* [docs/v2-context.md](./docs/v2-context.md) - design notes and decision log

## License

GNU LGPL v3 - see [LICENSE](./LICENSE).
