# T1 evidence - Vexel.Telegram v2 skeleton + toolchain pin

Branch `fm/vexel-v2-t1`, target commit `0b48983`.
Stated exit gate for T1: **green multi-TFM `dotnet build`**.
No UI surface is in scope, so there is no visual artifact; the reviewer-facing surfaces here are the build transcript and the NuGet/consumer experience.

## What was exercised

| # | Artifact | Shows |
|---|---|---|
| 1 | `01-multi-tfm-build.log` | Clean `dotnet restore` + `dotnet build -c Release --no-restore` (all `bin`/`obj` deleted first) on SDK `11.0.100-preview.6.26359.118` - the exact CI sequence. **0 warnings, 0 errors**, exit 0, under `TreatWarningsAsErrors` + `AnalysisLevel 9-all`. |
| 2 | `02-solution-shape-and-outputs.txt` | Solution builds exactly the 5 v2 projects; legacy `Abstractions`/`Commands`/`Interactivity`/`Extensions` + old sample `.csproj` files are still on disk but out of the build. Per-TFM assemblies emitted for `net8.0`/`net9.0`/`net10.0`/`net11.0`, plus `netstandard2.0` for the Generators analyzer. |
| 3 | `03-handlers-package.nuspec` | The packaged consumer contract: `lib/net8.0`..`lib/net11.0`, `repository branch="v2"`, `icon.png` at package root, and `Immediate.Handlers 3.11.1` listed as a **public** dependency in every TFM group (i.e. no `PrivateAssets`). |
| 4 | `04-consumer-e2e.log` | End-to-end downstream consumer: a scratch host app that references **only** `Vexel.Telegram.Handlers` + `Vexel.Telegram.Hosting`, compiles on all four TFMs, resolves `Immediate.Handlers.Shared` types transitively, calls `AddTelegramService(...)`, boots a real generic host, logs from the `VexelClient` shell, and shuts down cleanly (exit 0) on both .NET 10 and .NET 11. |

## Intent constraints verified

- `global.json` pins SDK `11.0.100-preview.6.26359.118`, `rollForward: latestFeature` - the build ran on that SDK.
- Multi-TFM `net8.0;net9.0;net10.0;net11.0`, `AnalysisLevel` pinned to `9-all`, `RepositoryBranch` = `v2`.
- `Remora.Results.Analyzers` is no longer a `GlobalPackageReference`; only `Meziantou.Analyzer` remains global. Remora `PackageVersion` entries are retained for T14, as intended.
- Generators is a single-TFM `netstandard2.0` Roslyn component and is consumed by Handlers as an analyzer (`ReferenceOutputAssembly=false`).
- Metapackage `Vexel.Telegram` is `IsPackable=false` - `dotnet pack` on it produces no `.nupkg`.
- CI `build.yml` triggers on `master`/`v2`/`v2/**`/`fm/**` and installs 8/9/10/11 with `dotnet-quality: preview`.

## Note on one nuspec detail

`Microsoft.Extensions.DependencyInjection.Abstractions` appears in the `net8.0`/`net9.0`/`net10.0` dependency groups but not `net11.0`.
This is not a packaging defect: .NET 11's `Microsoft.NETCore.App` reference pack now ships
`Microsoft.Extensions.DependencyInjection.Abstractions.dll` inbox, so the SDK prunes it from the
`net11.0` group. The project's declared dependencies are identical across all four TFMs
(confirmed in `project.assets.json`), and the net11.0 consumer run in artifact 4 works.
