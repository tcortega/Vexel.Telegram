# Public surface after the debloat (slices A + B)

Every type an application can see from the shipped packages, read back off the compiled
assemblies with reflection. This is what a consumer compiles against.

## Vexel.Telegram.Client

```text
class Vexel.Telegram.Client.BotCommandDescriptor
interface Vexel.Telegram.Client.Dispatch.IUpdateCompletionHook
interface Vexel.Telegram.Client.Dispatch.IUpdateRouter
interface Vexel.Telegram.Client.Dispatch.IUpdateScopeInitializer
class Vexel.Telegram.Client.Dispatch.UpdateDispatcher
static class Vexel.Telegram.Client.Dispatch.UpdateLaneKey  [FromUpdate]
class Vexel.Telegram.Client.Dispatch.UpdateScheduler
static class Vexel.Telegram.Client.Extensions.ServiceCollectionExtensions  [AddRawUpdateHandler, AddVexelTelegramClient]
interface Vexel.Telegram.Client.IBotCommandCatalog
interface Vexel.Telegram.Client.IRawUpdateHandler
enum Vexel.Telegram.Client.TelegramReceiveMode
class Vexel.Telegram.Client.VexelClient
class Vexel.Telegram.Client.VexelClientOptions
class Vexel.Telegram.Client.Webhook.WebhookOptions
class Vexel.Telegram.Client.Webhook.WebhookUpdateReceiver
```

## Vexel.Telegram.Handlers

```text
class Vexel.Telegram.Handlers.Attributes.CallbackAttribute
class Vexel.Telegram.Handlers.Attributes.ChosenInlineResultAttribute
class Vexel.Telegram.Handlers.Attributes.CommandAttribute
class Vexel.Telegram.Handlers.Attributes.InlineQueryAttribute
class Vexel.Telegram.Handlers.Attributes.OnCallbackQueryAttribute
class Vexel.Telegram.Handlers.Attributes.OnChosenInlineResultAttribute
class Vexel.Telegram.Handlers.Attributes.OnInlineQueryAttribute
class Vexel.Telegram.Handlers.Attributes.OnMessageAttribute
class Vexel.Telegram.Handlers.Contexts.CallbackContext
class Vexel.Telegram.Handlers.Contexts.ChosenInlineResultContext
class Vexel.Telegram.Handlers.Contexts.InlineQueryContext
class Vexel.Telegram.Handlers.Contexts.MessageContext
class Vexel.Telegram.Handlers.Contexts.UpdateContextHolder
static class Vexel.Telegram.Handlers.DependencyInjection.ServiceCollectionExtensions  [AddTelegramBot]
class Vexel.Telegram.Handlers.Feedback
class Vexel.Telegram.Handlers.Flow
class Vexel.Telegram.Handlers.FlowEntry
class Vexel.Telegram.Handlers.FlowOptions
static class Vexel.Telegram.Handlers.HandlerServiceCollectionExtensions  [AddVexelTelegramHandlersBehaviors, AddVexelTelegramHandlersHandlers]
interface Vexel.Telegram.Handlers.IFlowStore
static class Vexel.Telegram.Handlers.Keyboards.CallbackData  [EnsureWithinLimit, Format, IsWithinLimit]
class Vexel.Telegram.Handlers.Keyboards.InlineKeyboardBuilder
class Vexel.Telegram.Handlers.MemoryFlowStore
static class Vexel.Telegram.Handlers.Routing.CommandArgumentBinder  [TryParseBool, TryParseDecimal, TryParseEnum, TryParseInt, TryParseLong, TryTokenize]
class Vexel.Telegram.Handlers.Routing.OnHandlerEntry
class Vexel.Telegram.Handlers.Routing.RouteBinder
class Vexel.Telegram.Handlers.Routing.TelegramRouteContribution
class Vexel.Telegram.Handlers.Routing.TelegramRouter
static class Vexel.Telegram.Handlers.TelegramServiceCollectionExtensions  [AddVexelTelegramHandlersTelegram]
```

## Vexel.Telegram.Hosting

```text
static class Vexel.Telegram.Hosting.Extensions.ServiceCollectionExtensions  [AddTelegramService]
class Vexel.Telegram.Hosting.SetMyCommandsInitializer
class Vexel.Telegram.Hosting.VexelService
```

## Vexel.Telegram.AspNetCore

```text
static class Vexel.Telegram.AspNetCore.Extensions.TelegramWebhookEndpointExtensions  [MapTelegramWebhook]
```

## Gone from the public surface

| Symbol | Kind |
| --- | --- |
| `IUpdateDispatcher` | type: deleted or internal |
| `CallbackAnswerObligation` | type: deleted or internal |
| `InlineAnswerObligation` | type: deleted or internal |
| `AnswerObligation` | type: deleted or internal |
| `CommandRouteMetadata` | type: deleted or internal |
| `BotCommandRegistration` | type: deleted or internal |
| `RawUpdateHandlerRegistry` | type: deleted or internal |
| `UpdateContextScopeInitializer` | type: deleted or internal |
| `CommandKeyExtractor` | type: deleted or internal |
| `CallbackKeyExtractor` | type: deleted or internal |
| `InlineQueryKeyExtractor` | type: deleted or internal |
| `AddTelegramRouter` | DI helper: internal |
| `AddTelegramFlow` | DI helper: internal |
| `AddVexelUpdateContexts` | DI helper: internal |
| `IHostBuilder.AddTelegramService` | overload: deleted |

