# Vexel.Telegram v2 T2 - end-to-end evidence

A real generic host runs `VexelService` -> `VexelClient` against an in-process fake Telegram Bot API
server over HTTP. Updates travel the full production path a live bot uses:

`getUpdates` long poll -> `VexelClient` receive loop -> `UpdateScheduler` per-chat lane -> raw handler.

The handler and the library's own logs write to one timestamped transcript, so ordering, parallelism,
fault isolation and drain-on-shutdown are visible directly rather than inferred from assertions.

Reproduce:

```
dotnet test tests/Vexel.Telegram.Tests/Vexel.Telegram.Tests.csproj -f net10.0 \
  --filter "FullyQualifiedName~EndToEnd" -l "console;verbosity=detailed"
```

## 1. Ordering, cross-chat parallelism, fault isolation, shutdown drain

`PollingEndToEndTests.Bot_OrdersPerChat_RunsChatsInParallel_IsolatesFaults_AndDrainsOnShutdown`

Scenario: 2 stale updates are already queued when the bot starts. Then chat 100 gets three messages
(`/slow` 400 ms, `/boom` throws, `/after-boom`) and chat 200 gets one message, all in a single poll
batch. Finally one more chat-100 message starts handling and the host is asked to stop while it runs.

```text
[ 0.026s] fake Telegram Bot API listening on http://127.0.0.1:57285 with 2 stale updates queued
[ 0.071s] Information Lifetime         Application started. Press Ctrl+C to shut down.
[ 0.073s] Information Lifetime         Hosting environment: Production
[ 0.073s] Information Lifetime         Content root path: /Users/shiki/.no-mistakes/worktrees/b6147a1cfc26/01KZ6DYHBVB9FWYFYP9RE18S0V/tests/Vexel.Telegram.Tests/bin/Debug/net10.0/
[ 0.073s] Information VexelClient      VexelClient starting polling (DropPendingUpdates=True, LaneCapacity=64)
[ 0.113s] telegram-api  getUpdates(offset=-1) -> update 901
[ 0.196s] telegram-api  getUpdates(offset=902) -> no updates
[ 0.217s] user sends 3 messages in chat 100 and 1 message in chat 200 (single poll batch)
[ 0.308s] telegram-api  getUpdates(offset=902) -> update 902, update 903, update 904, update 905
[ 0.313s] telegram-api  getUpdates(offset=906) -> no updates
[ 0.315s] bot handler   chat 100 update 902 START  "/slow"
[ 0.315s] bot handler   chat 200 update 905 START  "/hello from another chat"
[ 0.316s] bot handler   chat 200 update 905 END
[ 0.448s] telegram-api  getUpdates(offset=906) -> no updates
[ 0.549s] telegram-api  getUpdates(offset=906) -> no updates
[ 0.682s] telegram-api  getUpdates(offset=906) -> no updates
[ 0.718s] bot handler   chat 100 update 902 END
[ 0.719s] bot handler   chat 100 update 903 START  "/boom"
[ 0.719s] bot handler   chat 100 update 903 THREW InvalidOperationException
[ 0.720s] Error       UpdateDispatcher Raw update handler Vexel.Telegram.Tests.EndToEnd.PollingEndToEndTests+ScriptedRawHandler failed for update 903 (InvalidOperationException: handler blew up)
[ 0.720s] bot handler   chat 100 update 904 START  "/after-boom"
[ 0.720s] bot handler   chat 100 update 904 END
[ 0.790s] user sends 1 more message in chat 100, then the host is asked to shut down
[ 0.790s] telegram-api  getUpdates(offset=906) -> update 906
[ 0.790s] bot handler   chat 100 update 906 START  "/in-flight at shutdown"
[ 0.791s] telegram-api  getUpdates(offset=907) -> no updates
[ 0.792s] Information Lifetime         Application is shutting down...
[ 1.209s] bot handler   chat 100 update 906 END
[ 1.209s] Information VexelClient      VexelClient stopped
[ 1.209s] host stopped

Telegram Bot API calls served:
  getUpdates offset=-1 -> [901]
  getUpdates offset=902 -> []
  getUpdates offset=902 -> [902,903,904,905]
  getUpdates offset=906 -> []
  getUpdates offset=906 -> []
  getUpdates offset=906 -> []
  getUpdates offset=906 -> []
  getUpdates offset=906 -> [906]
  getUpdates offset=907 -> []
```

What the transcript shows:

| Intent constraint | Evidence in the transcript |
| --- | --- |
| `DropPendingUpdates = true` default | `getUpdates(offset=-1) -> update 901`, then polling resumes at `offset=902`; the stale backlog (900, 901) never reaches a handler |
| Ordered per chat | chat 100 runs `902 END` -> `903 START` -> `903 THREW` -> `904 START`; no two chat-100 handlers overlap |
| Cross-chat parallelism / slow handler does not block | chat 200's update 905 starts and finishes at `0.315s`-`0.316s`, while chat 100's slow update 902 holds its lane until `0.718s` |
| Per-update fault isolation | update 903 throws `InvalidOperationException`; `UpdateDispatcher` logs it, the polling loop and the lane survive, and update 904 runs immediately after |
| Drain on shutdown | update 906 is mid-handler when shutdown begins; it reaches `END` before `VexelClient stopped` |

Telegram Bot API calls served during the run are listed at the end of the transcript
(`getUpdates offset=... -> [...]`), showing the real offset progression a live bot performs.

## 2. Bounded lanes apply backpressure and never drop updates

`PollingEndToEndTests.Bot_UnderBurst_NeverDropsUpdates_WhenLanesAreFull`

Scenario: `LaneCapacity = 2` and a single burst of 20 messages into one chat, so the lane buffer is
overrun many times over and the receive loop has to wait for capacity.

```text
[ 0.006s] Information Lifetime         Application started. Press Ctrl+C to shut down.
[ 0.006s] Information Lifetime         Hosting environment: Production
[ 0.006s] Information Lifetime         Content root path: /Users/shiki/.no-mistakes/worktrees/b6147a1cfc26/01KZ6DYHBVB9FWYFYP9RE18S0V/tests/Vexel.Telegram.Tests/bin/Debug/net10.0/
[ 0.006s] Information VexelClient      VexelClient starting polling (DropPendingUpdates=True, LaneCapacity=2)
[ 0.035s] user floods chat 100 with 20 messages while LaneCapacity=2
[ 0.119s] bot handler   chat 100 update 902 START  "burst 902"
[ 0.194s] bot handler   chat 100 update 902 END
[ 0.195s] bot handler   chat 100 update 903 START  "burst 903"
[ 0.210s] bot handler   chat 100 update 903 END
[ 0.210s] bot handler   chat 100 update 904 START  "burst 904"
[ 0.249s] bot handler   chat 100 update 904 END
[ 0.249s] bot handler   chat 100 update 905 START  "burst 905"
[ 0.285s] bot handler   chat 100 update 905 END
[ 0.285s] bot handler   chat 100 update 906 START  "burst 906"
[ 0.331s] bot handler   chat 100 update 906 END
[ 0.331s] bot handler   chat 100 update 907 START  "burst 907"
[ 0.371s] bot handler   chat 100 update 907 END
[ 0.371s] bot handler   chat 100 update 908 START  "burst 908"
[ 0.446s] bot handler   chat 100 update 908 END
[ 0.446s] bot handler   chat 100 update 909 START  "burst 909"
[ 0.499s] bot handler   chat 100 update 909 END
[ 0.499s] bot handler   chat 100 update 910 START  "burst 910"
[ 0.568s] bot handler   chat 100 update 910 END
[ 0.569s] bot handler   chat 100 update 911 START  "burst 911"
[ 0.643s] bot handler   chat 100 update 911 END
[ 0.643s] bot handler   chat 100 update 912 START  "burst 912"
[ 0.716s] bot handler   chat 100 update 912 END
[ 0.716s] bot handler   chat 100 update 913 START  "burst 913"
[ 0.790s] bot handler   chat 100 update 913 END
[ 0.790s] bot handler   chat 100 update 914 START  "burst 914"
[ 0.865s] bot handler   chat 100 update 914 END
[ 0.865s] bot handler   chat 100 update 915 START  "burst 915"
[ 0.882s] bot handler   chat 100 update 915 END
[ 0.882s] bot handler   chat 100 update 916 START  "burst 916"
[ 0.899s] bot handler   chat 100 update 916 END
[ 0.899s] bot handler   chat 100 update 917 START  "burst 917"
[ 0.964s] bot handler   chat 100 update 917 END
[ 0.965s] bot handler   chat 100 update 918 START  "burst 918"
[ 1.004s] bot handler   chat 100 update 918 END
[ 1.004s] bot handler   chat 100 update 919 START  "burst 919"
[ 1.067s] bot handler   chat 100 update 919 END
[ 1.067s] bot handler   chat 100 update 920 START  "burst 920"
[ 1.101s] bot handler   chat 100 update 920 END
[ 1.101s] bot handler   chat 100 update 921 START  "burst 921"
[ 1.138s] bot handler   chat 100 update 921 END
[ 1.233s] Information Lifetime         Application is shutting down...
[ 1.234s] Information VexelClient      VexelClient stopped
```

All 20 updates (902-921) were handled exactly once, strictly in send order, with none dropped and no
lane restart - the enqueue awaited capacity instead.
