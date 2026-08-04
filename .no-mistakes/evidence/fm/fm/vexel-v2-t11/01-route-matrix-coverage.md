# T11 unit matrix: what it now covers, run offline

Targeted run of the route / dispatch / flow unit namespaces on the fake bot client.
Every HTTP proxy variable was pointed at a dead port (`127.0.0.1:1`) with `NO_PROXY` cleared,
so any test that reached the network - even loopback - would have failed. All of them passed.

```
$ HTTP_PROXY=http://127.0.0.1:1 HTTPS_PROXY=http://127.0.0.1:1 ALL_PROXY=http://127.0.0.1:1 NO_PROXY= \
    dotnet test tests/Vexel.Telegram.Tests -f net10.0 --no-build \
    --filter 'FullyQualifiedName~Vexel.Telegram.Tests.Routing|...Dispatch|...Flow'

Test Run Successful.
Total tests: 204
     Passed: 204
```

Same filter on `net11.0`: 204 passed. The timing-sensitive `Dispatch` set was
repeated 5x back to back with no flake.

`+ new` marks a test method added by this commit (62 new methods; several are
`[Theory]`, so the case count is higher).

## Commands - key extraction, @suffix, arguments

**Routing.CommandKeyExtractorTests**

- Accepts matching bot suffix
- Bare at command is rejected  `+ new`
- Command without arguments yields empty args  `+ new`
- Empty text is not a command  `+ new`
- Extract is idempotent for common shapes  `+ new`
- Extracts plain command and args
- Ignores caption only messages
- Mid message bot command entity is ignored  `+ new`
- Non command entity at offset zero is ignored  `+ new`
- Preserves command casing from message  `+ new`
- Rejects foreign bot suffix
- Split overload reports bot suffix separately  `+ new`
- Split overload reports null suffix when absent  `+ new`
- Trims argument whitespace  `+ new`
- Unknown bot username accepts suffixed command  `+ new`

**Routing.CommandArgumentBinderTests**

- Empty payload with trailing string yields empty token  `+ new`
- Empty payload without trailing string fails for required token  `+ new`
- Exact token count without trailing rest succeeds  `+ new`
- Leftover tokens fail when trailing does not take rest  `+ new`
- Missing non string token fails
- Multi token trailing rest preserves interior whitespace after first cut  `+ new`
- Parses primitives
- Single trailing string token is the whole payload  `+ new`
- Tokenize is stable under repeated calls  `+ new`
- Trailing string may be empty when earlier tokens present  `+ new`
- Trailing string takes rest
- TryParseBool is case insensitive  `+ new`
- TryParseBool rejects non bool tokens  `+ new`
- TryParseDecimal rejects bad input  `+ new`
- TryParseEnum is case insensitive and defined only  `+ new`
- TryParseEnum rejects undefined  `+ new`
- TryParseInt rejects non integers  `+ new`

## Callbacks - key|suffix convention, answer obligation

**Routing.CallbackKeyExtractorTests**

- Extract is stable across repeated calls  `+ new`
- Extracts key and suffix
- Format rejects blank and pipe in key  `+ new`
- Format then extract roundtrips key and suffix  `+ new`
- Format then extract roundtrips key only  `+ new`
- Null data is not extractable

**Routing.CallbackRouterTests**

- Already answered rejection of default answer is not a warning
- Answer then throw sends no second answer
- Callback updates do not warn when no callback routes are registered
- Duplicate cross assembly callback keys fail fast
- Empty record callback ignores suffix
- Null callback data is unrouted but answered
- Routes callback key and suffix to binder
- Successful handler answer is not duplicated by obligation
- Throw before answer sends exactly one default answer
- Unmatched callback key logs below warning
- Unrouted callback is answered and does not invoke other binders

## Inline query / chosen inline result

**Routing.InlineQueryKeyExtractorTests**

- Extract is idempotent  `+ new`
- Extracts first token and trimmed remainder
- Never uses inline query id shape as input  `+ new`
- Single token with trailing whitespace has empty remainder  `+ new`

**Routing.InlineRouterTests**

- Already answered rejection of default answer is not a warning
- Answer then throw sends no second answer
- Chosen inline result routes on result id prefix
- Chosen inline result without suffix binds empty string
- Duplicate cross assembly chosen keys fail fast
- Duplicate cross assembly default inline handlers fail fast
- Duplicate cross assembly inline keys fail fast
- Empty or unmatched query routes to default with full query text
- Inline updates do not warn when no inline routes are registered
- Never routes on Inline query id
- Successful handler answer is not duplicated by obligation
- Throw before answer sends exactly one default empty answer
- Trigger routes on first token with remainder payload
- Unrouted inline query is answered when no default handler

## On* observer fan-out

**Routing.OnFanOutTests**

- Callback On handlers run after routed callback
- Cancellation stops On fan out midway  `+ new`
- Chosen On handlers run after routed chosen result  `+ new`
- Cross assembly On handlers are sorted by FQ name not assembly order
- Duplicate On contribution from one assembly fails fast
- Inline On handlers run after routed inline query  `+ new`
- On handler fault does not skip remaining On handlers
- On handlers run even when no route matches
- On handlers run in fully qualified metadata name order
- On handlers run when the routed stage faults on infrastructure
- On handlers still run when routed command handler throws  `+ new`
- On observers sharing a display name across assemblies are allowed
- Routed handler runs before On handlers

## Router matrix + shared failure paths

**Routing.RouterMatrixTests**

- Binding failure logs warning and does not throw  `+ new`
- Callback and inline misses still discharge answer obligations  `+ new`
- Cancellation during routed stage does not run On handlers  `+ new`
- Caption never command routes even with caption entities  `+ new`
- Handler exception is isolated per kind  `+ new`
- Routed hit then On then raw order is stable for every kind  `+ new`

**Routing.TelegramRouterTests**

- Binding failure logs warning and skips handler body  `+ new`
- BotUsernameOverride skips GetMe for suffixed commands  `+ new`
- CommandMetadata is sorted by name  `+ new`
- Command handler exception is isolated and does not throw  `+ new`
- Command keys match case insensitively  `+ new`
- Duplicate cross assembly flow steps fail fast  `+ new`
- Duplicate cross assembly keys fail fast
- GetMe failure is retried and does not latch
- Plain command does not call GetMe
- Raw only update kinds do not invoke message routes or OnMessage  `+ new`
- Routes command to binder
- Same assembly contributed twice points at the double registration
- Skips unknown command
- Suffixed command for another bot is skipped
- Suffixed command for this bot is routed  `+ new`

**Routing.TelegramRouterRegistrationTests**

- App registered router does not suppress the Telegram router
- Router is registered once even when called twice
- Telegram router resolves when an app router is registered last

## Flow - precedence, lifecycle, stale/failed steps

**Flows.FlowRouterTests**

- Arm then text invokes flow binder
- Built in cancel clears state and confirms
- Built in cancel stays silent without registered flow steps
- Built in cancel without armed flow stays silent
- Caption binds as flow payload
- Command for another bot mid flow does not eat message
- Flow step binding failure keeps state armed  `+ new`
- Known command wins over armed flow step  `+ new`
- Message without From skips flow step  `+ new`
- Stale step key clears state without invoking handler  `+ new`
- Step throw can opt out of generic error reply  `+ new`
- Step throw keeps armed and sends generic reply
- Success with cancel inside step does not rearm  `+ new`
- Success with rearm keeps new step
- Success without rearm auto completes
- Unknown slash command mid flow does not eat message
- User defined cancel command wins over built in

**Flows.FlowTests**

- CancelAsync clears store
- Draft round trips json poco
- PromptAsync arms step with default ttl

**Flows.MemoryFlowStoreTests**

- Abandoned entries are swept by later writes
- Complete removes entry
- Expired entry is cleared on read
- Get is non destructive
- Sweep keeps entries rearmed after expiry check

## Dispatch - lane keys and per-chat scheduler order

**Dispatch.UpdateLaneKeyTests**

- CallbackQuery FallsBackToFromId WhenChatless
- CallbackQuery UsesMessageChatId WhenPresent
- ChannelPost UsesChatId  `+ new`
- ChosenInlineResult UsesFromId
- EditedMessage UsesChatId  `+ new`
- Empty update uses global lane  `+ new`
- InlineQuery UsesFromId
- Lane key is stable across repeated reads  `+ new`
- Message UsesChatId
- MyChatMember UsesChatId  `+ new`
- Poll UsesGlobalLane

**Dispatch.UpdateSchedulerTests**

- Backpressure WaitsWhenLaneIsFull
- CrossChat UpdatesRunInParallel
- Dispatcher IsolatesHandlerResolutionFaults
- Dispatcher IsolatesRawHandlerFaults
- Dispatcher ResolvesScopedHandlersPerUpdate
- Dispatcher RunsDualRegisteredHandlerOnce
- Dispatcher RunsHealthyHandlerWhenAnotherFailsToConstruct
- Dispose DrainsBufferedUpdates
- Dispose IsIdempotentAndRejectsFurtherScheduling
- FaultIsolation ExceptionDoesNotKillLaneOrOtherChats
- IdleLanes AreEvicted
- Per chat order holds under interleaved multi chat schedule  `+ new`
- SameChat UpdatesAreSerializedInOrder
- SlowHandler InOneChat DoesNotBlockOtherChat
- StopAsync DispatchesBufferedUpdatesWhileContainerIsAlive
- StopAsync DrainsBufferedUpdatesAndLeavesSchedulerUsable
- StopAsync WindsDownWhenShutdownBudgetExpires

