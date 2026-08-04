# Do the new tests actually catch regressions?

Passing tests only prove the suite is green. To show the hardened matrix has teeth, eight
deliberate one-edit regressions were injected into the *product* code, one at a time - each
one breaks a behavior a Telegram user would feel. After every injection the same targeted
unit filter was rebuilt and rerun, then the source file was restored.

**All 8 regressions were caught. None survived.**

| # | Injected regression | Product file | Tests that turned red |
|---|---|---|---|
| 1 | A command miss (`/foo`) falls through and is eaten as flow-step input (D7/C2 broken) | `TelegramRouter.cs` | 1 failed (0 added by this commit) |
| 2 | `/ping@ThisBot` no longer has its @suffix stripped, so suffixed commands stop routing | `CommandKeyExtractor.cs` | 9 failed (5 added by this commit) |
| 3 | Callback data is never split on `\|`, so `key\|suffix` payloads stop routing and binding | `CallbackKeyExtractor.cs` | 15 failed (9 added by this commit) |
| 4 | The B4 fail-closed hook skips the default answerCallbackQuery when nothing answered, so the spinner hangs | `CallbackAnswerObligation.cs` | 6 failed (1 added by this commit) |
| 5 | On* observers fan out in reverse fully-qualified-name order (D9/N3 broken) | `TelegramRouter.cs` | 3 failed (1 added by this commit) |
| 6 | A throwing On* observer is rethrown instead of isolated, killing the rest of the fan-out (P2 broken) | `TelegramRouter.cs` | 1 failed (0 added by this commit) |
| 7 | Callback lanes key off From.Id instead of the chat id, so a chat loses its ordering lane | `UpdateLaneKey.cs` | 1 failed (0 added by this commit) |
| 8 | The lane worker stops awaiting dispatch, so same-chat updates can complete out of order | `UpdateScheduler.cs` | 7 failed (1 added by this commit) |

## 1. command-precedence-over-flow

*A command miss (`/foo`) falls through and is eaten as flow-step input (D7/C2 broken)* - edit in `src/Vexel.Telegram.Handlers/Routing/TelegramRouter.cs`

```
Failed Flows.FlowRouterTests.Unknown_slash_command_mid_flow_does_not_eat_message
```

## 2. command-at-suffix-matching

*`/ping@ThisBot` no longer has its @suffix stripped, so suffixed commands stop routing* - edit in `src/Vexel.Telegram.Handlers/Routing/CommandKeyExtractor.cs`

```
Failed Routing.TelegramRouterTests.GetMe_failure_is_retried_and_does_not_latch
Failed Routing.CommandKeyExtractorTests.Unknown_bot_username_accepts_suffixed_command  <- new
Failed Routing.CommandKeyExtractorTests.Bare_at_command_is_rejected  <- new
Failed Routing.CommandKeyExtractorTests.Accepts_matching_bot_suffix
Failed Routing.CommandKeyExtractorTests.Split_overload_reports_bot_suffix_separately  <- new
Failed Routing.CommandKeyExtractorTests.Rejects_foreign_bot_suffix
Failed Routing.TelegramRouterTests.Suffixed_command_for_another_bot_is_skipped
Failed Routing.TelegramRouterTests.Suffixed_command_for_this_bot_is_routed  <- new
Failed Routing.TelegramRouterTests.BotUsernameOverride_skips_GetMe_for_suffixed_commands  <- new
```

## 3. callback-key-suffix-split

*Callback data is never split on `|`, so `key|suffix` payloads stop routing and binding* - edit in `src/Vexel.Telegram.Handlers/Routing/CallbackKeyExtractor.cs`

```
Failed Routing.CallbackKeyExtractorTests.Extracts_key_and_suffix(data: "|only-suffix", expectedKey: "", expectedSuffix: "only-suffix")
Failed Routing.CallbackKeyExtractorTests.Extracts_key_and_suffix(data: "confirm|42", expectedKey: "confirm", expectedSuffix: "42")
Failed Routing.CallbackKeyExtractorTests.Extracts_key_and_suffix(data: "pick|a|b|c", expectedKey: "pick", expectedSuffix: "a|b|c")
Failed Routing.CallbackKeyExtractorTests.Format_then_extract_roundtrips_key_and_suffix(routeKey: "x", suffix: "suffix with spaces")  <- new
Failed Routing.CallbackKeyExtractorTests.Format_then_extract_roundtrips_key_and_suffix(routeKey: "menu", suffix: "")  <- new
Failed Routing.CallbackKeyExtractorTests.Format_then_extract_roundtrips_key_and_suffix(routeKey: "ok", suffix: "1")  <- new
Failed Routing.CallbackKeyExtractorTests.Format_then_extract_roundtrips_key_and_suffix(routeKey: "pick", suffix: "a|b|c")  <- new
Failed Routing.CallbackKeyExtractorTests.Extract_is_stable_across_repeated_calls  <- new
Failed Routing.RouterMatrixTests.Binding_failure_logs_warning_and_does_not_throw(kind: "chosen")  <- new
Failed Routing.RouterMatrixTests.Binding_failure_logs_warning_and_does_not_throw(kind: "callback")  <- new
Failed Routing.CallbackRouterTests.Routes_callback_key_and_suffix_to_binder
Failed Routing.CallbackRouterTests.Empty_record_callback_ignores_suffix
Failed Routing.OnFanOutTests.Chosen_On_handlers_run_after_routed_chosen_result  <- new
Failed Routing.RouterMatrixTests.Routed_hit_then_On_then_raw_order_is_stable_for_every_kind  <- new
Failed Routing.InlineRouterTests.Chosen_inline_result_routes_on_result_id_prefix
```

## 4. callback-answer-obligation

*The B4 fail-closed hook skips the default answerCallbackQuery when nothing answered, so the spinner hangs* - edit in `src/Vexel.Telegram.Handlers/Routing/CallbackAnswerObligation.cs`

```
Failed Routing.CallbackRouterTests.Throw_before_answer_sends_exactly_one_default_answer
Failed Routing.RouterMatrixTests.Callback_and_inline_misses_still_discharge_answer_obligations  <- new
Failed Routing.CallbackRouterTests.Routes_callback_key_and_suffix_to_binder
Failed Routing.CallbackRouterTests.Unrouted_callback_is_answered_and_does_not_invoke_other_binders
Failed Routing.CallbackRouterTests.Already_answered_rejection_of_default_answer_is_not_a_warning
Failed Routing.CallbackRouterTests.Null_callback_data_is_unrouted_but_answered
```

## 5. on-star-fanout-order

*On* observers fan out in reverse fully-qualified-name order (D9/N3 broken)* - edit in `src/Vexel.Telegram.Handlers/Routing/TelegramRouter.cs`

```
Failed Routing.OnFanOutTests.Cross_assembly_On_handlers_are_sorted_by_FQ_name_not_assembly_order
Failed Routing.OnFanOutTests.On_handlers_run_in_fully_qualified_metadata_name_order
Failed Routing.OnFanOutTests.Cancellation_stops_On_fan_out_midway  <- new
```

## 6. on-star-fault-isolation

*A throwing On* observer is rethrown instead of isolated, killing the rest of the fan-out (P2 broken)* - edit in `src/Vexel.Telegram.Handlers/Routing/TelegramRouter.cs`

```
Failed Routing.OnFanOutTests.On_handler_fault_does_not_skip_remaining_On_handlers
```

## 7. callback-lane-key

*Callback lanes key off From.Id instead of the chat id, so a chat loses its ordering lane* - edit in `src/Vexel.Telegram.Client/Dispatch/UpdateLaneKey.cs`

```
Failed Dispatch.UpdateLaneKeyTests.CallbackQuery_UsesMessageChatId_WhenPresent
```

## 8. per-chat-dispatch-ordering

*The lane worker stops awaiting dispatch, so same-chat updates can complete out of order* - edit in `src/Vexel.Telegram.Client/Dispatch/UpdateScheduler.cs`

```
Failed Dispatch.UpdateSchedulerTests.SameChat_UpdatesAreSerializedInOrder
Failed Dispatch.UpdateSchedulerTests.StopAsync_WindsDownWhenShutdownBudgetExpires
Failed Dispatch.UpdateSchedulerTests.StopAsync_DrainsBufferedUpdatesAndLeavesSchedulerUsable
Failed Dispatch.UpdateSchedulerTests.Dispose_DrainsBufferedUpdates
Failed Dispatch.UpdateSchedulerTests.Backpressure_WaitsWhenLaneIsFull
Failed Dispatch.UpdateSchedulerTests.StopAsync_DispatchesBufferedUpdatesWhileContainerIsAlive
Failed Dispatch.UpdateSchedulerTests.Per_chat_order_holds_under_interleaved_multi_chat_schedule  <- new
```

Every mutation was reverted; `git status` is clean and the suite is green again
(see `01-route-matrix-coverage.md`).
