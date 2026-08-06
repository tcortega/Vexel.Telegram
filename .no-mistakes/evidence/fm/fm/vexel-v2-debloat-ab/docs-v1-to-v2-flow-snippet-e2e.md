# `docs/v1-to-v2.md`: the Flow snippet, run as a conversation

The guide's Flow statements were spliced character for character into real handlers, so
`PromptAsync<TRequest>`, `GetDraftAsync`, `SetDraftAsync` and `CancelAsync` had to exist and
behave as printed for this conversation to happen.

## Snippet, exactly as `docs/v1-to-v2.md` prints it

```csharp
// arm the next step (from a command, a callback, or an earlier step)
await flow.PromptAsync<CollectName.Command>(cancellationToken: token);

// inside the armed step handler (its request carries the user's text): read, mutate, write back
var draft = await flow.GetDraftAsync<SignupDraft>(token) ?? new SignupDraft();
draft.Name = command.Name.Trim();
await flow.SetDraftAsync(draft, token);
await flow.PromptAsync<CollectAge.Command>(cancellationToken: token);

await flow.CancelAsync(token); // or built-in /cancel
```

## The chat

```text
user  >  /signup            (arms the first step)
user  >  Ada Lovelace       (armed step: draft written, next step armed)
user  >  36                 (success without re-arm -> auto-completes)
user  >  /signup            (arms again)
user  >  /bail              (command wins mid-flow; CancelAsync clears it)
user  >  Ada again          (nothing armed -> unrouted, bot stays silent)
```

Bot -> Telegram, as the Telegram side saw it:

```text
setMyCommands commands=[/bail - "Explicit cancel from the doc snippet"; /signup - "Doc flow snippet"]
deleteWebhook drop_pending_updates=true
sendMessage chat_id=100 text="What's your name?"
sendMessage chat_id=100 text="Hi Ada Lovelace, how old are you?"
sendMessage chat_id=100 text="Registered Ada Lovelace, age 36. Flow complete."
sendMessage chat_id=100 text="What's your name?"
sendMessage chat_id=100 text="Bailed."
```
