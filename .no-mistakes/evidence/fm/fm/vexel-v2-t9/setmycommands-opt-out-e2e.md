# SetMyCommands opt-out (`VexelClientOptions.RegisterBotCommands = false`)

The same bot, with the same two `[Command]`-annotated handlers, started with the opt-out
flag set. Telegram sees no `setMyCommands`, so the BotFather menu is left alone.

Bot -> Telegram Bot API calls, in order:

```text
setWebhook url="https://bot.example.test/telegram/webhook" secret_token="vexel-webhook-secret_01" drop_pending_updates=true
```
