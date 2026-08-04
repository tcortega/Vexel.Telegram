# Host fail-fast: Both polling and webhook configured

```text
[ 0.014s] Information Lifetime         Application started. Press Ctrl+C to shut down.
[ 0.014s] Critical    VexelService     Fatal error while running the Vexel Telegram client; stopping host. (InvalidOperationException: VexelClientOptions is configured for both polling and webhook. Set ReceiveMode to Webhook and configure Webhook, or leave ReceiveMode as Polling and set Webhook to null.)
[ 0.014s] Error       Host             BackgroundService failed (InvalidOperationException: VexelClientOptions is configured for both polling and webhook. Set ReceiveMode to Webhook and configure Webhook, or leave ReceiveMode as Polling and set Webhook to null.)
[ 0.014s] Information Lifetime         Hosting environment: Production
[ 0.014s] Information Lifetime         Content root path: /Users/shiki/.no-mistakes/worktrees/b6147a1cfc26/01KZ740VS4X4H1KR2Y4Z1KR0D8/tests/Vexel.Telegram.Tests/bin/Debug/net10.0/
[ 0.014s] Critical    Host             The HostOptions.BackgroundServiceExceptionBehavior is configured to StopHost. A BackgroundService has thrown an unhandled exception, and the IHost instance is stopping. To avoid this behavior, configure this to Ignore; however the BackgroundService will not be restarted. (InvalidOperationException: VexelClientOptions is configured for both polling and webhook. Set ReceiveMode to Webhook and configure Webhook, or leave ReceiveMode as Polling and set Webhook to null.)
[ 0.014s] Information Lifetime         Application is shutting down...
[ 0.014s] host application lifetime signalled ApplicationStopping at start (no zombie host)
```

