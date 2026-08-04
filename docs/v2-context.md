# Vexel.Telegram V2 - design context

Status: design archaeology and decision log for V2, grown out of the captain design discussion.
Not a frozen API contract.
Items below are labeled `agreed`, `proposed`, or `open`.
Do not treat proposed items as authorization to implement product code.

Last updated: 2026-08-04 (decision log through 32; T1 skeleton, T2 client dispatch, T4 contexts/Feedback/DI, T3 `[Command]` routing, and T5 `[Callback]` + keyboard helpers landed)
Branch: `v2` (created to hold this context and future V2 work)
Repo stays public. Private-repo idea was rejected.

---

## 1. Why V2 exists

Captain intent:

- Keep what is good about Vexel today: design direction, speed, concurrency standards.
- Reject what hurts: excessive complexity, reflection-heavy command/interaction stack, broken inline queries.
- Align with Immediate Platform philosophy: source generation over reflection, compile-time pipelines, great DX.
- Captain is on the Immediate Platform development team and already uses Immediate.Handlers + Immediate.Apis for HTTP.
- Goal shape: **Immediate.Apis, but for Telegram** (working name in discussion: Immediate.Telegram / Vexel as Telegram transport on Immediate.Handlers).

Non-goals called out so far:

- Do not make the repository private.
- Do not start a vague "improve everything" survey outside routed work.
- Do not treat this document as a frozen API contract.

---

## 2. Current codebase snapshot (research)

Local clone at discussion time tracked `master` @ `4f67638` (FeedbackService readme work).

### Packages

| Package | Role today |
|---------|------------|
| `Vexel.Telegram` | Metapackage |
| `Vexel.Telegram.Abstractions` | Responder contracts |
| `Vexel.Telegram.Client` | Client, responder dispatch, concurrency-related wiring |
| `Vexel.Telegram.Commands` | Remora.Commands integration, contexts, feedback, prefix, execution events, command registrar |
| `Vexel.Telegram.Interactivity` | Interaction groups, callback/inline/chosen/text attributes, conversation/state helpers |
| `Vexel.Telegram.Hosting` | Generic host background service |
| `Vexel.Telegram.Extensions` | Builders (e.g. inline keyboard) |

Heavily inspired by Remora.Discord. Command/interaction execution goes through **Remora.Commands** (runtime tree, prepare, execute).

### What is worth keeping (proposed)

- Concurrent update dispatch philosophy and client/host lifecycle.
- Feedback helpers idea (reply/edit/success-style UX).
- Keyboard / builder ergonomics.
- Explicit contexts injectable via DI.
- Raw responder-style escape hatch for non-routed update handling.
- Public library bar: tests, API stability mindset, quality over speed of hacking.

### What should die in V2 (proposed)

- Remora.Commands as the execution engine.
- `CommandGroup` / `InteractionGroup` multi-method reflection bags as the primary app model.
- Runtime command-tree registration ceremony (`AddCommandTree().WithCommandGroup<>()` chains) as the happy path.
- Any design that cannot be made compile-time safe for route maps and handler wiring.

Remora.Results is separable from Remora.Commands. Keeping Results is **open** (see decisions).

---

## 3. Concrete bug: inline queries

Finding from code read (high confidence this is broken on current master):

`InteractivityResponder` for `InlineQuery` does:

1. `InteractionIdHelper.IsFromInteractionTree(update.Id)`
2. Only then parses path/parameters from that id and executes the interaction command tree.

Telegram's `InlineQuery.Id` is an **opaque server id**, not developer callback data.
It will not start with the library interaction-tree prefix (e.g. `vexel::...`).

So handlers like sample `[InlineQuery("search")]` effectively never run via this path.

Contrast:

- Callback queries correctly use `update.Data` (developer-provided).
- Chosen inline results use `update.ResultId` (can be developer-provided when answering inline).
- Inline **queries** must route on **query text** (and empty/default query), not on Telegram's query id.

`InteractionIdHelper.CreateInlineQueryId` exists, but that does not fix inbound routing of user-typed inline input.

Fixed in V2 only; no parallel master hotfix (decision 25).

---

## 4. Immediate Platform research

### Immediate.Handlers

- Source-generated mediator.
- `[Handler]` on a type, nested request record, `HandleAsync`, generated `Type.Handler`, behaviors pipeline, DI registration `AddXxxHandlers()`.
- Behaviors assembly-wide or per-handler; constraints supported.
- Discovery is **exact**:
  - `HandlerAttribute` is **`sealed`**.
  - Generator uses `ForAttributeWithMetadataName("Immediate.Handlers.Shared.HandlerAttribute", ...)`.
  - `IsHandlerAttribute` checks name + `Immediate.Handlers.Shared` namespace only.
- **Inheritance from `HandlerAttribute` does not work today** and would not be picked up without an upstream change.

### Immediate.Apis

- Sibling source generator for ASP.NET Core minimal APIs.
- Pattern: `[Handler]` + `[MapGet("/route")]` (dual attributes).
- Does **not** reimplement handlers; maps HTTP transport -> build request -> `Handler.HandleAsync`.
- Ships analyzer/code fix when map attribute present without `[Handler]` (`IAPI0001`).
- Proves dual-generator cooperation in the **app** project: Handlers gen + Apis gen both see user source; they do **not** consume each other's generated output.

### Other Immediate packages (context only)

Validations, Cache, Injections, Jobs exist under ImmediatePlatform.
Not committed for Vexel V2 unless a later decision pulls them in.

### Implication for Vexel

Vexel should play **Apis' role for Telegram**, not fork a second mediator:

| Layer | Owner |
|-------|--------|
| Handler engine, behaviors, DI for handlers | Immediate.Handlers |
| Telegram route attributes, route table gen, bind update -> request, bot host, contexts, feedback | Vexel |
| Receive loop, concurrency, hosting | Vexel.Client / Hosting |

Cross-generator note: a Vexel generator **cannot** emit `[Handler]` on a partial and expect Immediate.Handlers to see it in the same compilation. Single-attribute UX needs either dual attrs (Apis parity) or an **upstream Immediate.Handlers** discovery change (feasible politically; captain is on that team).

---

## 5. Target architecture (proposed)

### One-liner

Vexel V2 = Immediate.Apis for Telegram: Immediate.Handlers for work units; Vexel for routes, binding, contexts, feedback, and bot host.

### Package layout - **agreed** (decision 20), skeleton shipped in T1

```
Vexel.Telegram                  metapackage (Client + Handlers + Hosting); IsPackable=false until publish decision
Vexel.Telegram.Client           receive (poll/webhook), dispatch, concurrency
Vexel.Telegram.Hosting          generic host integration
Vexel.Telegram.Handlers         Telegram attributes, contexts, feedback, conversation helpers, builders
Vexel.Telegram.Generators       source generator + analyzers + code fixes
                                  (netstandard2.0; not published standalone, packed into Handlers)
```

No separate `Abstractions` or `Extensions` package in V2.0.
Current build-level details live in `AGENTS.md`; the solution and csproj files are authoritative.

App references: `Vexel.Telegram` + `Immediate.Handlers` as an **explicit peer** (same honesty as Apis).

### Runtime flow - **command + callback legs settled in T3/T5**

```
Update
  -> Client (concurrency / ordering policies)
    -> generated Telegram router
         match kind + key (command | callback | inline trigger | chosen result | ...)
         bind to TRequest
         resolve generated Immediate handler
         await HandleAsync
    -> raw IRawUpdateHandler escape hatches (always last; cannot suppress routing)
    -> IUpdateCompletionHook (B4: default answerCallbackQuery if Feedback did not answer;
                              the answerInlineQuery leg is planned with the inline leg in T7)
```

Hot path: compile-time map, no reflection invoke.

`[Command]` and `[Callback]` legs exist today: `IUpdateRouter` is the dispatcher seam, `TelegramRouter`
the runtime implementation. Inline / chosen-result legs are still proposed.
Binding conventions live in the code that implements them -
`CommandKeyExtractor`, `CommandArgumentBinder`, `CallbackKeyExtractor` in
`src/Vexel.Telegram.Handlers/Routing` - not restated here.
Keyboard builders emit short `key` / `key|suffix` callback data (`Keyboards/`).

### Example DX (illustrative, not approved API names)

Dual attribute (Apis parity, works with Immediate today):

```csharp
[Handler]
[Command("ping")]
public static partial class Ping
{
    public sealed record Command;

    private static async ValueTask HandleAsync(
        Command _,
        IFeedback feedback,
        CancellationToken ct)
    {
        await feedback.ReplyAsync("Pong!", ct);
    }
}
```

Desired end-state if Immediate allows transport attributes to imply handler:

```csharp
[Command("ping")]
public static partial class Ping { /* same body */ }
```

Inline query routing must key off query text:

```csharp
[Handler]
[InlineQuery("search")]
public static partial class SearchInline
{
    public sealed record Query(string Text);
    // ...
}
```

Callback state remains developer payload in callback data, not Telegram opaque ids.

### Contexts / feedback - **settled in T4**

Small injectables, not a giant framework. Concrete types (decision 31), no interfaces:

- `MessageContext`, `CallbackContext`, `InlineQueryContext`, `ChosenInlineResultContext`,
  bound per update by a write-once scoped `UpdateContextHolder` and exposed as scoped factories.
  Resolving a context for the wrong update kind throws.
- `Feedback` for reply/edit/answer helpers - keep the v1 idea, thinner API.
  Answer methods are first-wins idempotent and latch only after the Bot API send succeeds.
  `Edit` needs a bot-authored message (callback or chosen inline result).

Shapes and invariants live with the code in `src/Vexel.Telegram.Handlers`; see `AGENTS.md` for the
one-paragraph summary.

Conversation state: explicit service or payload state; avoid magic interceptors unless designed cleanly later.

### Behaviors

Prefer Immediate behaviors for cross-cutting (auth, logging, validation host hooks).
Do not invent a second pipeline.

### Registration DX

```csharp
builder.Services.AddTelegramBot(_ => "<BOT_TOKEN>");    // client + host + contexts + Feedback + router
builder.Services.AddXxxHandlers();                      // Immediate
builder.Services.AddXxxTelegram();                      // Vexel generated routes
```

**Settled in T4:** `AddTelegramBot(...)` ships (with `AddVexelUpdateContexts()` as the contexts-only
seam for manual wiring).
**Settled in T3:** the generator emits `Add{Assembly}Telegram()`, which registers that assembly's
`TelegramRouteContribution`; `TelegramRouter` composes all contributions and fails fast on duplicate
command keys across assemblies.

### Escape hatch

Raw update handlers without Immediate for power users and non-command traffic.
**Settled in T2:** `IRawUpdateHandler` (non-generic, takes the whole `Update`), registered with
`AddRawUpdateHandler<THandler>()`; the dispatcher runs raw handlers last, isolated per handler.

---

## 6. Attribute / DX decisions

### Dual `[Handler]` + Telegram attribute - **agreed (option A)**

V2 ships **Immediate.Apis parity**: every routed Telegram handler type **must** carry:

1. `[Handler]` (Immediate.Handlers) - required for handler/pipeline/DI generation
2. A Vexel Telegram attribute (`[Command]`, `[Callback]`, `[InlineQuery]`, …) - required for route-table generation

Users are forced to use `[Handler]`. That is an accepted product cost, not a temporary oversight.

Reasons this locked:

- `HandlerAttribute` is sealed; FAWMN matches exact metadata name only (CA1813 + Roslyn cookbook favor this).
- Upstream Immediate.Handlers will not take pay-as-you-go extra marker discovery / FAWMN extension for this (issue outcome).
- **Rejected:** public fork of Immediate.Handlers; vendoring Immediate as a hidden internal fork.
- **Rejected for V2.0:** option B (Vexel owns full handler engine) as the starting path.
- Option B remains a **possible later major** if dual-attr pain justifies owning pipeline gen. Design A so migration stays feasible: Vexel owns Telegram attrs + routing; mirror Immediate handler shape; do not leak Immediate types through Vexel’s Telegram-facing APIs beyond the package peer dependency.

Mitigations for dual-attr DX:

- Analyzer + code fix when a Vexel route attr is present without `[Handler]` (same idea as Immediate.Apis IAPI0001).
  **T3:** the analyzer ships (`VEX0001`); the code fix is still outstanding.
  Shipped rule IDs and severities are tracked in
  `src/Vexel.Telegram.Generators/AnalyzerReleases.*.md`, not duplicated here.
- Samples and docs always show the pair; never document Telegram attrs alone as sufficient.

### Do not

- Hide Immediate as a private implementation detail while leaking its types.
- Fork or publish a competing Immediate.Handlers.
- Build a second full mediator in V2.0 just to save one attribute line.
- Over-abstract packages before the sample bot feels good.

### Simple vs power users

- Simple: one handler file, `[Handler]` + route attr, feedback + context inject, copy sample.
- Power: behaviors, tags/lifetimes, manual handler inject, raw update handlers, full Telegram.Bot access.

---

## 7. Branch / shipping strategy

| Topic | Status | Note |
|-------|--------|------|
| Keep repo public | **agreed** | Captain rejected private |
| Long-lived `v2` branch | **agreed** | This branch; master remains v1 line |
| NuGet: v1 from master, v2 breaking major later | **agreed** | Decision 27; publish timing still open |
| Rewrite on `v2` rather than piecemeal master break | **agreed** | Decision 26 |
| Tiny master inline hotfix in parallel | **rejected** | Decision 25; effort goes to `v2` only |

---

## 8. Decision log

### Agreed

1. V2 work happens on branch `v2`, repository stays public.
2. Direction is Immediate-style source generation, not more Remora.Commands reflection.
3. **Option A:** Immediate.Handlers is the handler engine (open peer dependency, Apis-style composition), not a buried or forked rewrite.
4. Vexel owns Telegram transport binding, host, concurrency, contexts, feedback, builders, and route-table source generation.
5. **Dual attributes required:** `[Handler]` + Vexel route attribute on every routed handler type. Users must use `[Handler]`.
6. No public fork of Immediate.Handlers; no vendoring Immediate as a hidden fork for V2.0.
7. Inline queries must route on query text / empty default, not `InlineQuery.Id` interaction-tree checks.
8. DX remains first-class within option A: analyzers/code fix for missing `[Handler]`, samples as spec.
9. This context file is committed so design chat can switch topics without losing research.
10. Option B (Vexel-owned handler engine, single Telegram attr) is deferred; revisit only if dual-attr cost justifies it. Keep A→B migration door open via stable Vexel attrs and Immediate-compatible handler shape.
11. Real-user E2E on Telegram **test DC** (user MTProto client + test bot), plus unit fakes in CI. Not prod server userbots.
12. One handler type per route (Immediate style). No multi-method CommandGroup/InteractionGroup bags.
13. No Remora.Results. Handlers use ValueTask/ValueTask<T>; errors via exceptions (+ optional behaviors).
14. Route attrs: `[Command]`, `[Callback]`, `[InlineQuery]`, `[ChosenInlineResult]`, plus first-class text/conversation routing (not deferred).
15. Common-workflow-first DX: commands, callback menus, inline mode, multi-step text prompts/FSM - not only fire-and-forget commands.
16. UX model: button-first (`[Callback]`), commands as entry/deep link/cancel, typed `IFlow.PromptAsync<TNext>` for text steps, per-scope draft/store for multi-question state. No reflection. No Immediate.Cache required for flow (that package is response caching, not FSM). Pluggable `IFlowStore` (memory sample, redis/etc prod).
17. `IFeedback` thin high-DX helper (not optional framework sludge): Reply/Edit/AnswerCallback/AnswerInline + send-with-keyboard. Defaults from context (chat, message id, parse mode opt). Power: inject `ITelegramBotClient` anytime. No fat localization/template engine in core.
18. Concurrency: configurable; **default ordered per chat**, cross-chat parallel. Power can loosen.
19. Hosting: **polling default**, webhook supported.
20. Packages (V2.0): **Client + Handlers(+embedded generators/analyzers) + Hosting**. Merge/skip separate Abstractions/Extensions packages initially; builders live with Handlers or Client. Metapackage optional at publish time.
21. TFMs: **same as Immediate.Handlers** (`net8.0;net9.0;net10.0;net11.0`). Depend on **latest stable Telegram.Bot** at implement time.
22. Callback data: **short stable route id**; multi-step/large state in store/draft. Analyzer for 64B cap. Optional tiny suffix when needed.
23. `SetMyCommands`: **auto** from `[Command]`+description at startup; opt-out flag for power users.
24. Boot DX: `AddTelegramBot(...)` wires client/host/Vexel routes; app still calls Immediate `AddXxxHandlers()`. Sample shows both. No hiding Immediate.
25. No v1 master inline hotfix parallel track; effort on `v2` only.
26. Drop Remora.Commands (and Remora.Results) on `v2` entirely.
27. v1→v2: **breaking major**; sample + short differences doc. No compat shims.
28. On-update fan-out **kept** (v1 responder usefulness): `[OnMessage]`/`[OnCallback]`/… as Immediate `[Handler]`s; **Vexel gen** emits static multi-cast dispatch. No reflection responder bus. No Immediate.Notifications (N/A). Behaviors apply per handler. Default **await** on per-chat pipeline; fire-and-forget opt-in only.
29. Dispatch order (per chat): **routed handler first** (command/callback/flow text); **then** `[On*]` fan-out always (observe/side-effect). On* must not replace routing.
30. On-update attrs: `[OnMessage]`, `[OnCallbackQuery]`, `[OnInlineQuery]`, `[OnChosenInlineResult]`, … (On* prefix).
31. Prefer **concrete** contexts/`Feedback`/`Flow` (no interface-for-mocking). Keep **`IFlowStore`** (and Telegram.Bot’s `ITelegramBotClient`) where swap is real. No unit-test-driven interface soup.
32. E2E: `tests/Vexel.Telegram.E2E` (test DC user+bot); light `tests/Vexel.Telegram.Tests`. Secrets: **GitHub Actions secrets** + local **dotnet user-secrets**. Never commit sessions/tokens.




### Proposed (not fully approved)

1. Callback payload format and size/analyzer rules.
2. Metapackage vs explicit package references guidance for production apps.
3. Delivery cadence (skeleton -> gen -> sample -> delete dead v1 surface on branch).

## 12. Test strategy - **agreed direction**

Layers:

1. Unit: fake bot client / in-mem updates (CI always).
2. **Real-user E2E (primary integration path):** MTProto **user** client + Vexel **bot** on Telegram **test DCs** (not production).

Test DC facts:

- Separate env from prod (my.telegram.org / client test mode).
- Test phones: `+99966XYYYY` (X=DC 1-3, YYYY=0000-9999); login code often DC digit repeated (e.g. DC2 → `22222`).
- BotFather on test server → **separate** test bot token (prod token useless there).
- C# user client candidate: **WTelegramClient** (`server_address` → test DC).
- Bot side: normal Bot API against test-environment bot; long poll simplest for agents/CI.

Harness shape (proposed detail, direction agreed):

- Secrets: test `api_id`/`api_hash`, user session (or disposable test phone auth), test bot token, fixed test chat id.
- Tests/scripts: user client sends commands/callbacks/inline as human; assert bot replies/edits.
- CI: private runner or manual/nightly with secrets; never commit sessions.
- Agents driving V2 work use this harness as the end-user path.

Not a substitute for unit tests. Not prod userbots.

---

### Open questions

None tracked here.
The charter is frozen and delivery runs through the numbered T-task plan (T1 skeleton, T2 client dispatch, T4 contexts/Feedback/DI, T3 `[Command]` routing landed).


---

## 9. Recommended delivery sequence (proposed)

1. Freeze charter from this doc once captain approves enough of section 5-6.
2. PlanScout (Fable 5 / Cloud Code per secondmate captain prefs): attribute set, binding rules, payload format, package boundaries, sample matrix, non-goals.
3. Adversary review (Pi / Grok).
4. Implement on `v2`: empty skeleton, generator, router, sample bot, tests.
5. Delete or stop porting dead Remora.Commands surface on `v2` only.
6. Docs/samples as DX gate before any NuGet v2 talk.

Secondmate home pipeline prefs (operational, not product API):

- PlanScout: Fable 5 on Cloud Code
- Adversary + implementation: Pi (Grok 4.5 default)

---

## 10. References

- Repo: https://github.com/tcortega/Vexel.Telegram
- Immediate.Handlers: https://github.com/ImmediatePlatform/Immediate.Handlers
- Immediate.Apis: https://github.com/ImmediatePlatform/Immediate.Apis
- ImmediatePlatform org: https://github.com/ImmediatePlatform
- Notable local paths at research time:
  - `src/Vexel.Telegram.Interactivity/Responders/InteractivityResponder.InlineQuery.cs`
  - `src/Vexel.Telegram.Interactivity/InteractionIdHelper.cs`
  - `src/Vexel.Telegram.Commands/Responders/CommandResponder.cs`
  - `samples/Vexel.Telegram.Sample/Interactions/SampleInteractions.cs`
  - Immediate discovery: `Immediate.Handlers` `HandlerAttribute` (sealed), `ImmediateHandlersGenerator` `ForAttributeWithMetadataName`, `ITypeSymbolExtensions.IsHandlerAttribute`

---

## 11. Document maintenance

- Update this file when design decisions flip from proposed -> agreed, or when open questions close.
- Prefer rewriting sections over appending contradictory notes.
- Implementation details that become real code should eventually live in normal docs/samples; this file remains the design archaeology and decision log for V2.
