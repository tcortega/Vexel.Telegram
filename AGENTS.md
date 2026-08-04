# Project agent memory

This file is the project's committed home for project-intrinsic agent knowledge: build, test, release, architecture, and sharp-edge notes that should travel with the code.

- Add durable project-specific notes here as they are discovered through real work.

## V2 rewrite (branch `v2` / `fm/*`)

* Toolchain: `global.json` pins SDK 11 preview with `rollForward: latestFeature`. Multi-TFM is `net8.0;net9.0;net10.0;net11.0` in `Directory.Build.props`. `AnalysisLevel` is pinned (`9-all`), not `latest-all`, because TreatWarningsAsErrors + unpinned latest breaks on new SDK waves.
* Package layout: `Client` (receive/dispatch), `Handlers` (attrs/contexts/routing; packs generator later), `Generators` (netstandard2.0 Roslyn, not published alone), `Hosting` (BackgroundService), metapackage `Vexel.Telegram` (`IsPackable=false` until publish decision).
* Handlers references `Immediate.Handlers` as a normal PackageReference (no PrivateAssets) so consumers get it transitively.
* Legacy v1 projects under `src/Vexel.Telegram.{Abstractions,Commands,Interactivity,Extensions}` and the old sample stay on disk as reference only until T14; they are out of the solution build.
* Central package versions live in `Directory.Packages.props`. Microsoft.Extensions is on 10.0.x to satisfy Immediate.Handlers TFM floors.
* `docs/v2-context.md` owns the v2 design decisions and decision log; check it before proposing API or architecture changes.

## Maintaining this file

Keep this file for knowledge useful to almost every future agent session in this project.
Do not repeat what the codebase already shows; point to the authoritative file or command instead.
Prefer rewriting or pruning existing entries over appending new ones.
When updating this file, preserve this bar for all agents and keep entries concise.
