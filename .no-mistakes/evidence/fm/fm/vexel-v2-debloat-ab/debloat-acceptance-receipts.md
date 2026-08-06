# Debloat slices A + B: acceptance receipts

Base `13a81e2` -> head `1f7ffb8`, checked against the code in each tree.
The behavioural proof lives in `sample-bot-session-e2e.md` (a full bot session on the wire) and
`consumer-compile-surface.md` (what an outside project's compiler now says).

| Acceptance item | Base `13a81e2` | Head `1f7ffb8` |
| --- | --- | --- |
| Twin obligations are one type | `CallbackAnswerObligation.cs` + `InlineAnswerObligation.cs`, both `public sealed class` | one `internal sealed class AnswerObligation : IUpdateCompletionHook` |
| `IUpdateDispatcher` deleted | `public interface IUpdateDispatcher` | file gone; DI and callers take concrete `UpdateDispatcher` |
| Dual raw-handler registration gone | dispatcher had `provider.GetServices<IRawUpdateHandler>()` plus a degraded/fallback resolution path | dispatcher only walks `RawUpdateHandlerRegistry.HandlerTypes`, filled by `AddRawUpdateHandler<T>` |
| Multi-catalog enumeration gone | `IEnumerable<IBotCommandCatalog> catalogs` in `SetMyCommandsInitializer` | single `IBotCommandCatalog? catalog = null` |
| Multi-router composition gone | routers composed from `GetServices<IUpdateRouter>()` | single router; a foreign `IUpdateRouter` fails fast at `AddTelegramRouter` or at dispatcher resolve |
| `CommandRouteMetadata` unified away | `public sealed record CommandRouteMetadata` + generator emitted it | generator emits `BotCommandDescriptor` (see route-table snapshots) |
| `BotCommandRegistration` inlined | `public static class BotCommandRegistration` | file gone; payload building is `SetMyCommandsInitializer.BuildPayload` (internal) |
| `IHostBuilder.AddTelegramService` deleted | `HostBuilderExtensions.AddTelegramService(this IHostBuilder, ...)` | only `ServiceCollectionExtensions.AddTelegramService(this IServiceCollection, ...)` |
| Extractors internal | `public static class CommandKeyExtractor` / `CallbackKeyExtractor` / `InlineQueryKeyExtractor` | all three `internal static class` |
| Peel DI internal | `public` `AddTelegramRouter` / `AddTelegramFlow` / `AddVexelUpdateContexts` | all three `internal`; `AddTelegramBot` is the only public composition call |
| Registry + scope initializer demoted | `public sealed class RawUpdateHandlerRegistry` / `UpdateContextScopeInitializer` | both `internal sealed class` |
| Unused `PackageVersion` entries removed | `Microsoft.Extensions.Caching.Memory` 10.0.10, `WTelegramClient` 4.4.7 | both deleted from `Directory.Packages.props` |
| Public seams kept | - | `IRawUpdateHandler`, `IFlowStore`, `IUpdateRouter`, `IBotCommandCatalog`, `IUpdateScopeInitializer`, `IUpdateCompletionHook`, `CommandArgumentBinder`, `AddTelegramBot`, `AddRawUpdateHandler`, contexts / `Feedback` / `Flow` / keyboards - all still exported (`public-surface-after-debloat.md`) |
| Docs note the breaks (slice C) | - | `docs/v1-to-v2.md`: raw handlers "registered via `AddRawUpdateHandler<T>`", single `IUpdateRouter` and "registering your own `IUpdateRouter` throws", "There is no `IHostBuilder` overload" |
