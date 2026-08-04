# Project agent memory

This file is the project's committed home for project-intrinsic agent knowledge: build, test, release, architecture, and sharp-edge notes that should travel with the code.

- Add durable project-specific notes here as they are discovered through real work.

## V2 rewrite (branch `v2` / `fm/*`)

* Toolchain: `global.json` pins SDK 11 preview with `rollForward: latestFeature`. Multi-TFM is `net8.0;net9.0;net10.0;net11.0` in `Directory.Build.props`. `AnalysisLevel` is pinned (`9-all`), not `latest-all`, because TreatWarningsAsErrors + unpinned latest breaks on new SDK waves.
* Package layout: `Client` (receive/dispatch), `Handlers` (attrs/contexts/routing; packs generator later), `Generators` (netstandard2.0 Roslyn, not published alone), `Hosting` (BackgroundService), metapackage `Vexel.Telegram` (`IsPackable=false` until publish decision).
* Client dispatch: `VexelClient` long-polls via Telegram.Bot `ReceiveAsync`; `UpdateScheduler` runs ordered-per-chat lanes (bounded await-backpressure, idle eviction); `IUpdateDispatcher` is the routed → On* → raw seam (`IUpdateRouter`s run first, On* still pending); handler faults are isolated per update/handler and never kill the lane or polling loop.
* Lane keys: message chat id; callback `Message?.Chat.Id ?? From.Id`; inline/chosen `From.Id`; otherwise global (`UpdateLaneKey`).
* Per-update DI: one `AsyncServiceScope` per update; `IUpdateScopeInitializer` runs before handler resolve so Handlers can bind write-once `UpdateContextHolder`. Contexts + concrete `Feedback` are scoped factories over the holder (`AddTelegramBot` / `AddVexelUpdateContexts`).
* Contexts: `MessageContext`, `CallbackContext` (nullable `ChatId`), `InlineQueryContext`, `ChosenInlineResultContext`. `Feedback` is thin (Reply/Edit/AnswerCallback/AnswerInline/SendWithKeyboard); answer methods are first-wins idempotent and only latch after the Bot API send succeeds. `Edit` needs a bot-authored message (callback or chosen inline result); plain message updates throw.
* App DI entry: `AddTelegramBot(...)` (client + host + contexts + Feedback + `TelegramRouter`). Compose with Immediate `AddXxxHandlers()` + generated `AddXxxTelegram()`.
* Routing (T3): dual-attr Option A - `[Handler]` + `[Command("name")]`. Generator seeds FAWMN on `HandlerAttribute`, keeps types that also have a Vexel route attr; emits `Add{Assembly}Telegram()` registering a `TelegramRouteContribution` (frozen command map + binders that `GetRequiredService<X.Handler>()`). `TelegramRouter` implements `IUpdateRouter` and runs before raw handlers in `UpdateDispatcher`.
* Binding convention (normative in `CommandKeyExtractor` / `CommandArgumentBinder`): command key from offset-0 `BotCommand` entity; `@Bot` suffix stripped/matched; args bind by request ctor (empty / single string / tokenized primitives; trailing string takes rest). Analyzer rule IDs live in `src/Vexel.Telegram.Generators/AnalyzerReleases.*.md` - do not restate them here.
* Generator tests: `tests/.../Generators/` runs both Immediate + Vexel generators in-process (composition spike). Handlers types must be `partial`. Do not `InternalsVisibleTo` the Generators assembly from tests - PolySharp `ModuleInitializerAttribute` collides with `System.Runtime`.
* Tests live in `tests/Vexel.Telegram.Tests` (xunit): unit + generator snapshots + `EndToEnd/` host-level polling against an in-process fake Bot API; run with `dotnet test`.
* Handlers references `Immediate.Handlers` as a normal PackageReference (no PrivateAssets) so consumers get it transitively. Generators is Analyzer ProjectReference today; nupkg embedding is T15.
* Legacy v1 projects under `src/Vexel.Telegram.{Abstractions,Commands,Interactivity,Extensions}` and the old sample stay on disk as reference only until T14; they are out of the solution build.
* Central package versions live in `Directory.Packages.props`. Microsoft.Extensions is on 10.0.x to satisfy Immediate.Handlers TFM floors.
* `docs/v2-context.md` owns the v2 design decisions and decision log; check it before proposing API or architecture changes.

## Maintaining this file

Keep this file for knowledge useful to almost every future agent session in this project.
Do not repeat what the codebase already shows; point to the authoritative file or command instead.
Prefer rewriting or pruning existing entries over appending new ones.
When updating this file, preserve this bar for all agents and keep entries concise.
