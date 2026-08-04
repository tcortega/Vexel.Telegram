# T14: the parked v1 Remora surface is gone, and only the Immediate surface ships

Every block below is real output captured on this commit (`f4200b5`), not a description of it.

## 1. What left the tree

```console
$ git diff --stat c65f41b f4200b5 -- src | tail -1
 52 files changed, 2840 deletions(-)

$ ls src
Vexel.Telegram
Vexel.Telegram.AspNetCore
Vexel.Telegram.Client
Vexel.Telegram.Generators
Vexel.Telegram.Handlers
Vexel.Telegram.Hosting
```

`Vexel.Telegram.{Abstractions,Commands,Interactivity,Extensions}` no longer exist on disk.
The six remaining projects are exactly the v2 set.

## 2. No Remora or JetBrains reference survives anywhere it could ship

```console
$ grep -rn "Remora\|JetBrains" --include="*.props" --include="*.csproj" --include="*.sln" --include="*.cs" --include="*.yml" .
(no matches in build files, projects, or C# sources)
```

The only surviving mentions are prose in `README.md`, `docs/v1-to-v2.md` and `docs/v2-context.md`,
which describe the v1 heritage and the migration on purpose.

## 3. The whole solution still builds without the deleted projects

```console
$ dotnet build Vexel.Telegram.sln -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Nothing in the v2 packages, the sample bot, or the test project was reaching into the parked v1 code.
(`TreatWarningsAsErrors` is on, so this is a clean build, not a tolerated one.)

## 4. What a user actually installs

```console
$ dotnet pack Vexel.Telegram.sln -c Release -o /tmp/vexel-pack-t14
$ ls /tmp/vexel-pack-t14
Vexel.Telegram.AspNetCore.0.0.0-preview.0.42.nupkg
Vexel.Telegram.Client.0.0.0-preview.0.42.nupkg
Vexel.Telegram.Handlers.0.0.0-preview.0.42.nupkg
Vexel.Telegram.Hosting.0.0.0-preview.0.42.nupkg
```

Four packages, no `Vexel.Telegram.Abstractions`, `.Commands`, `.Interactivity` or `.Extensions`.
Their `net10.0` dependency groups, read straight out of the `.nuspec` inside each `.nupkg`:

```xml
<!-- Vexel.Telegram.Client -->
<dependency id="Microsoft.Extensions.DependencyInjection.Abstractions" version="10.0.10" />
<dependency id="Microsoft.Extensions.Logging.Abstractions" version="10.0.10" />
<dependency id="Microsoft.Extensions.Options" version="10.0.10" />
<dependency id="Telegram.Bot" version="22.10.2.1" />

<!-- Vexel.Telegram.Hosting -->
<dependency id="Vexel.Telegram.Client" version="0.0.0-preview.0.42" />
<dependency id="Microsoft.Extensions.Hosting.Abstractions" version="10.0.10" />
<dependency id="Microsoft.Extensions.Options" version="10.0.10" />

<!-- Vexel.Telegram.Handlers -->
<dependency id="Vexel.Telegram.Client" version="0.0.0-preview.0.42" />
<dependency id="Vexel.Telegram.Hosting" version="0.0.0-preview.0.42" />
<dependency id="Immediate.Handlers" version="3.11.1" />
<dependency id="Microsoft.Extensions.DependencyInjection.Abstractions" version="10.0.10" />
<dependency id="Microsoft.Extensions.Options" version="10.0.10" />
<dependency id="Telegram.Bot" version="22.10.2.1" />

<!-- Vexel.Telegram.AspNetCore -->
<dependency id="Vexel.Telegram.Client" version="0.0.0-preview.0.42" />
<dependency id="Microsoft.Extensions.Options" version="10.0.10" />
```

Immediate.Handlers, Telegram.Bot, Microsoft.Extensions. No `Remora.Commands`, `Remora.Results`,
`Remora.Results.Analyzers`, `Remora.Extensions.Options.Immutable`, or `JetBrains.Annotations`.

## 5. A fresh consumer installing those packages

A throwaway `net10.0` console app outside the repo, pointed at the packed output as its only local
feed, with a single `PackageReference` to `Vexel.Telegram.Handlers`:

```console
$ dotnet run --project /tmp/vexel-consumer-t14/consumer.csproj
AddTelegramBot() registered 31 services from the v2 package surface.
Vexel.Telegram.Handlers references: Microsoft.Extensions.DependencyInjection.Abstractions,
  Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.Options, System.Collections,
  System.Collections.Concurrent, System.Collections.Immutable, System.ComponentModel, System.Linq,
  System.Memory, System.Runtime, System.Text.Json, System.Threading, Telegram.Bot,
  Vexel.Telegram.Client, Vexel.Telegram.Hosting
No Remora/JetBrains assemblies anywhere in the loaded or referenced graph.

$ dotnet list /tmp/vexel-consumer-t14/consumer.csproj package --include-transitive
   Top-level Package              Requested            Resolved
   > Vexel.Telegram.Handlers      0.0.0-preview.0.42   0.0.0-preview.0.42

   Transitive Package                                           Resolved
   > Immediate.Handlers                                         3.11.1
   > Microsoft.Extensions.Configuration.Abstractions            10.0.10
   > Microsoft.Extensions.DependencyInjection.Abstractions      10.0.10
   > Microsoft.Extensions.Diagnostics.Abstractions              10.0.10
   > Microsoft.Extensions.FileProviders.Abstractions            10.0.10
   > Microsoft.Extensions.Hosting.Abstractions                  10.0.10
   > Microsoft.Extensions.Logging.Abstractions                  10.0.10
   > Microsoft.Extensions.Options                               10.0.10
   > Microsoft.Extensions.Primitives                            10.0.10
   > Telegram.Bot                                               22.10.2.1
   > Vexel.Telegram.Client                                      0.0.0-preview.0.42
   > Vexel.Telegram.Hosting                                     0.0.0-preview.0.42
```

The package restores, `AddTelegramBot` wires up, and the closure a user drags in is Immediate-based only.

The repo's own sample bot agrees - its full transitive graph on `net10.0` is the same set,
with no Remora entry:

```console
$ dotnet list samples/Vexel.Telegram.Sample/Vexel.Telegram.Sample.csproj package --include-transitive --framework net10.0
   > Immediate.Handlers  3.11.1   ...   > Telegram.Bot  22.10.2.1
```

## 6. The surviving surface still behaves

```console
$ dotnet test tests/Vexel.Telegram.Tests -c Release -f net10.0 \
    --filter "FullyQualifiedName~EndToEnd.SampleBotEndToEndTests"
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1
```

The full chat transcript that run produced is in `sample-bot-session-e2e.md` next to this file:
commands, callbacks, the multi-turn flow, inline queries and `[On*]` observers all still served
end to end with the v1 projects deleted.
