# Host fail-fast: Fatal client error while receiving

```text
[ 0.002s] Information Lifetime         Application started. Press Ctrl+C to shut down.
[ 0.002s] Information Lifetime         Hosting environment: Production
[ 0.002s] Information Lifetime         Content root path: /Users/shiki/.no-mistakes/worktrees/b6147a1cfc26/01KZ740VS4X4H1KR2Y4Z1KR0D8/tests/Vexel.Telegram.Tests/bin/Debug/net10.0/
[ 0.002s] host started; polling receive loop is running
[ 0.065s] Critical    VexelService     Fatal error while running the Vexel Telegram client; stopping host. (HttpRequestException: Telegram API unreachable)
[ 0.065s] Error       Host             BackgroundService failed (HttpRequestException: Telegram API unreachable)
[ 0.065s] Critical    Host             The HostOptions.BackgroundServiceExceptionBehavior is configured to StopHost. A BackgroundService has thrown an unhandled exception, and the IHost instance is stopping. To avoid this behavior, configure this to Ignore; however the BackgroundService will not be restarted. (HttpRequestException: Telegram API unreachable)
[ 0.065s] Information Lifetime         Application is shutting down...
[ 0.065s] host application lifetime signalled ApplicationStopping (no zombie host)
```

