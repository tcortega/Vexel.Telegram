# VEX0007: a bad `PromptAsync` target fails the build

The bot author writes this and hits Build:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Immediate.Handlers.Shared;
using Vexel.Telegram.Handlers;
using Vexel.Telegram.Handlers.Attributes;

namespace SignupBot;

public sealed record NotAHandlerRequest;

[Handler]
[Command("ping")]
public static class Ping
{
	public sealed record Command(string Text);

	private static ValueTask HandleAsync(Command request, CancellationToken token) => default;
}

[Handler]
public static class CollectCount
{
	public sealed record Command(int Count);

	private static ValueTask HandleAsync(Command request, CancellationToken token) => default;
}

[Handler]
public static class CollectName
{
	public sealed record Command(string Name);

	private static ValueTask HandleAsync(Command request, CancellationToken token) => default;
}

public static class Prompts
{
	public static ValueTask NotAHandler(Flow flow, CancellationToken token) =>
		flow.PromptAsync<NotAHandlerRequest>(cancellationToken: token);

	public static ValueTask RoutedHandler(Flow flow, CancellationToken token) =>
		flow.PromptAsync<Ping.Command>(cancellationToken: token);

	public static ValueTask UnbindableRequest(Flow flow, CancellationToken token) =>
		flow.PromptAsync<CollectCount.Command>(cancellationToken: token);

	public static ValueTask ValidStep(Flow flow, CancellationToken token) =>
		flow.PromptAsync<CollectName.Command>(cancellationToken: token);
}
```

Compiler output (error severity, so the build stops):

```text
Bot.cs(39,20): error VEX0007: Type 'SignupBot.NotAHandlerRequest' is not a registered flow step request; it must be the request of a [Handler] with no route attribute and be empty or take a single string (binding convention rule 5).
Bot.cs(42,20): error VEX0007: Type 'SignupBot.Ping.Command' is the request of a routed handler ('SignupBot.Ping' carries [Command]/[Callback]) and is not a flow step. Flow.PromptAsync targets a [Handler] with no route attribute (a pure text step).
Bot.cs(45,20): error VEX0007: Flow request 'SignupBot.CollectCount.Command' on 'CollectCount' must be an empty record or a single string parameter receiving Message.Text ?? Caption (binding convention rule 5).
```

`ValidStep`, which prompts the request of a routeless `[Handler]` with a single-string request, produces no diagnostic.

