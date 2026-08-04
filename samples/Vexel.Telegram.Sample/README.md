# Vexel.Telegram v2 sample bot

Living docs for the v2 surface.
Every routed type carries dual attributes (`[Handler]` + a Vexel route or `[On*]` attribute); pure flow steps are the one exception and carry `[Handler]` alone (`Handlers/SignupFlow.cs`).
Composition is the three-call pattern - nothing is hidden behind `AddTelegramBot`.

## What it demonstrates

| Surface | How to try it |
| --- | --- |
| Button-first menu | `/start` or `/menu` |
| Commands | `/ping`, `/echo hi`, `/help`, `/inline`, `/signup` |
| Callbacks | menu buttons; `pick_color\|red` binds the suffix |
| Flow | Sign up button or `/signup` → name → age; `/cancel` aborts |
| Inline | `@your_bot search cats`, bare `@your_bot hello` |
| Chosen inline result | pick a result from the inline list (logged) |
| On* observers | watch console logs after each routed update |

## Run

1. Create a bot with [@BotFather](https://t.me/BotFather) and copy the token.
   Enable inline mode if you want the inline demo (`/setinline`).
2. From this directory, supply the token **one** of these ways:

```bash
# env (good for CI-like shells)
export TELEGRAM_BOT_TOKEN='123456:ABC...'

# or user-secrets (persists for this project; UserSecretsId is in the csproj)
dotnet user-secrets set BotToken '123456:ABC...'
```

3. Run:

```bash
dotnet run --project samples/Vexel.Telegram.Sample
# or, from this directory:
dotnet run
```

4. Open a private chat with the bot and send `/start`.

### Token resolution order

1. Environment variable `TELEGRAM_BOT_TOKEN`
2. Configuration `BotToken` (user-secrets / `appsettings`)
3. Configuration `Telegram:BotToken`

Missing token → process exits with a clear exception before the host starts.

## Wiring (the three calls)

```csharp
builder.Services.AddTelegramBot(_ => token);                 // client + host + contexts + Feedback + Flow + router
builder.Services.AddVexelTelegramSampleHandlers();           // Immediate.Handlers
builder.Services.AddVexelTelegramSampleTelegram();           // Vexel-generated routes / flow steps / On*
```

Method names come from the assembly name (`Vexel.Telegram.Sample` → `VexelTelegramSample`).
Your app will get `Add{YourAssembly}Handlers` / `Add{YourAssembly}Telegram` the same way.

## Flow pitfall worth knowing

While a flow step is armed, **leading-`/` text is still command-routed first**.
`/cancel` and `/menu` work mid-signup on purpose.
An unknown `/foo` is a command miss: it does **not** bind as free text for the step.
Type the answer without a leading slash, or `/cancel` and restart.

## Test DC / E2E

Against a real bot token this sample is meant to be driven by hand today.
Its handler files are also compiled and driven as a whole session by `tests/Vexel.Telegram.Tests/EndToEnd/SampleBotEndToEndTests.cs` against an in-process fake Bot API, so the surfaces listed above stay honest.
A real Telegram test-DC E2E harness (shared secrets, `useTestEnvironment`, WTelegram user client) is **T12a** and needs captain-provisioned secrets - not part of this sample's run loop yet.
When that lands, this bot is the fixture the suite drives.

## Project references

The sample references the `Vexel.Telegram` metapackage project plus the generators analyzer project directly.
Package consumers will get the generator from the Handlers nupkg once packing lands (T15); project-reference analyzer refs do not flow transitively in the monorepo, so the sample wires the analyzer explicitly.
