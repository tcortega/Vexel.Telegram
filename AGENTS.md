# Project agent memory

This file is the project's committed home for project-intrinsic agent knowledge: build, test, release, architecture, and sharp-edge notes that should travel with the code.

- Add durable project-specific notes here as they are discovered through real work.

## V2 rewrite (branch `v2` / `fm/*`)

* Toolchain: `global.json` pins SDK 11 preview with `rollForward: latestFeature`. Multi-TFM is `net8.0;net9.0;net10.0;net11.0` in `Directory.Build.props`. `AnalysisLevel` is pinned (`9-all`), not `latest-all`, because TreatWarningsAsErrors + unpinned latest breaks on new SDK waves.
* Package layout: `Client` (receive/dispatch), `Handlers` (attrs/contexts/routing; packs generator later), `Generators` (netstandard2.0 Roslyn, not published alone), `Hosting` (BackgroundService), metapackage `Vexel.Telegram` (`IsPackable=false` until publish decision).
* Client dispatch: `VexelClient` long-polls via Telegram.Bot `ReceiveAsync`; `UpdateScheduler` runs ordered-per-chat lanes (bounded await-backpressure, idle eviction); `IUpdateDispatcher` is the routed → On* → raw seam (raw-only until router lands); handler faults are isolated per update/handler and never kill the lane or polling loop.
* Lane keys: message chat id; callback `Message?.Chat.Id ?? From.Id`; inline/chosen `From.Id`; otherwise global (`UpdateLaneKey`).
* Per-update DI: one `AsyncServiceScope` per update; `IUpdateScopeInitializer` runs before handler resolve so Handlers can bind write-once `UpdateContextHolder`. Contexts + concrete `Feedback` are scoped factories over the holder (`AddTelegramBot` / `AddVexelUpdateContexts`).
* Contexts: `MessageContext`, `CallbackContext` (nullable `ChatId`), `InlineQueryContext`, `ChosenInlineResultContext`. `Feedback` is thin (Reply/Edit/AnswerCallback/AnswerInline/SendWithKeyboard); answer methods are first-wins idempotent and only latch after the Bot API send succeeds. `Edit` needs a bot-authored message (callback or chosen inline result); plain message updates throw.
* App DI entry: `AddTelegramBot(...)` (client + host + contexts + Feedback). Still need Immediate `AddXxxHandlers()` + generated `AddXxxTelegram()` later.
* Tests live in `tests/Vexel.Telegram.Tests` (xunit): unit tests plus `EndToEnd/` host-level polling tests against an in-process fake Bot API; run with `dotnet test`.
* Handlers references `Immediate.Handlers` as a normal PackageReference (no PrivateAssets) so consumers get it transitively.
* Legacy v1 projects under `src/Vexel.Telegram.{Abstractions,Commands,Interactivity,Extensions}` and the old sample stay on disk as reference only until T14; they are out of the solution build.
* Central package versions live in `Directory.Packages.props`. Microsoft.Extensions is on 10.0.x to satisfy Immediate.Handlers TFM floors.
* `docs/v2-context.md` owns the v2 design decisions and decision log; check it before proposing API or architecture changes.

## Maintaining this file

Keep this file for knowledge useful to almost every future agent session in this project.
Do not repeat what the codebase already shows; point to the authoritative file or command instead.
Prefer rewriting or pruning existing entries over appending new ones.
When updating this file, preserve this bar for all agents and keep entries concise.
