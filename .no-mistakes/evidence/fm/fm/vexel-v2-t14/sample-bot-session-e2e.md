# Vexel.Telegram v2 sample bot, end to end

The sample's own handler files (`samples/Vexel.Telegram.Sample/Handlers/`: `Commands.cs`, `Inline.cs`, `MarkdownText.cs`, `Menu.cs`, `Observers.cs`, `SignupFlow.cs`) are read off disk, compiled with the Immediate and Vexel generators, registered with
the same three calls `Program.cs` makes, and run in a real host against a fake Telegram
Bot API server over HTTP. Every line below is a real message on the wire.

## 1. Wiring under test (the three calls from `Program.cs`)

```csharp
builder.Services.AddTelegramBot(_ => token);        // client + host + contexts + Feedback + Flow + router
builder.Services.AddVexelTelegramSampleHandlers();  // Immediate.Handlers
builder.Services.AddVexelTelegramSampleTelegram();  // Vexel-generated routes / flow steps / On*
```

## 2. The chat, as the user sees it

```text
user  >  types "/start"
  bot <  *Vexel.Telegram v2 sample*
         
         Button-first menu. Tap a row, or use a slash command.
         
         • *Ping* - callback route
         • *Sign up* - multi-turn `Flow.PromptAsync`
         • *Colors* - callback with a bound suffix
         • *Inline tip* - try `@bot search cats`
         • *Help* - what each piece demonstrates
         
         Commands always win over an armed flow, so `/menu` and `/cancel` work mid-signup.
         An unknown `/foo` mid-flow is a command miss (falls through to `On*`) - not free text.
         [ Ping ] [ Sign up ]
         [ Colors ] [ Help ]
         [ Try inline here ] [ Try inline anywhere ]
user  >  types "/ping"
  bot <  Pong!
user  >  types "/echo hi there"
  bot <  You said: hi there
user  >  types "/help"
  bot <  *What this sample shows*
         
         `[Handler]` + a Vexel route or `[On*]` attribute on every type (Immediate.Apis parity).
         
         • `/start`, `/menu` - button-first home keyboard
         • `/ping`, `/echo hi`, `/help`, `/inline`, `/signup`
         • Callbacks: `ping`, `signup`, `colors`, `pick_color|red`, `menu`, `help`
         • Flow: `/signup` or the Sign up button → name → age (`Flow.PromptAsync`)
         • Inline: `@bot search cats` and bare `@bot ...`
         • `[OnMessage]` observer logs every message after the routed leg
         
         Built-in `/cancel` clears an armed flow (unless you define your own `[Command("cancel")]`).
         
         Wiring in `Program.cs`:
         `AddTelegramBot` + `AddVexelTelegramSampleHandlers` + `AddVexelTelegramSampleTelegram`.
user  >  types "/inline"
  bot <  *Inline mode*
         
         Type `@your_bot search kittens` in any chat, or tap a button below.
         
         Empty / unmatched queries hit the default `[InlineQuery]` handler.
         Picking a result routes `[ChosenInlineResult("item")]`.
         [ Search here ]
         [ Search anywhere ]
user  >  taps [Ping] (callback_data "ping")
  bot <  [popup alert] Pong!
  bot <  (edits its message) Pong from a `[Callback]` handler at 22:15:43 UTC.
         [ Ping ] [ Sign up ]
         [ Colors ] [ Help ]
         [ Try inline here ] [ Try inline anywhere ]
user  >  taps [Colors] (callback_data "colors")
  bot <  (button spinner stops, no popup)
  bot <  (edits its message) Pick a color. The suffix after `|` binds to the handler.
         [ Red ] [ Green ] [ Blue ]
         [ Back ]
user  >  taps [Red] (callback_data "pick_color|red")
  bot <  [toast] Selected red
  bot <  (edits its message) chat100 picked *red*.
         [ Pick again ] [ Menu ]
user  >  types "/signup"
  bot <  What's your name?
         
         (Commands still win mid-flow: try /cancel. An unknown /foo is not free text.)
user  >  types "Ada Lovelace"
  bot <  Nice to meet you, Ada Lovelace. How old are you? (send a number)
user  >  types "/menu"
  bot <  *Vexel.Telegram v2 sample*
         
         Button-first menu. Tap a row, or use a slash command.
         
         • *Ping* - callback route
         • *Sign up* - multi-turn `Flow.PromptAsync`
         • *Colors* - callback with a bound suffix
         • *Inline tip* - try `@bot search cats`
         • *Help* - what each piece demonstrates
         
         Commands always win over an armed flow, so `/menu` and `/cancel` work mid-signup.
         An unknown `/foo` mid-flow is a command miss (falls through to `On*`) - not free text.
         [ Ping ] [ Sign up ]
         [ Colors ] [ Help ]
         [ Try inline here ] [ Try inline anywhere ]
user  >  types "twelve-ish"
  bot <  Something went wrong - try again, or /cancel.
user  >  types "36"
  bot <  Registered Ada Lovelace, age 36. Flow complete.
         
         /menu to go home.
user  >  taps [Sign up] (callback_data "signup")
  bot <  (button spinner stops, no popup)
  bot <  (edits its message) Starting signup…
  bot <  What's your name?
         
         (Commands still win mid-flow: try /cancel. An unknown /foo is not free text.)
user  >  types "/nope"
  bot <  (silence - nothing sent to the chat)
user  >  types "/cancel"
  bot <  Cancelled.
user  >  types "Ada again"
  bot <  (silence - nothing sent to the chat)
user  >  types "@vexel_bot search cats" in some chat
  bot <  [inline results] Search: cats | Docs hit for cats
user  >  types "@vexel_bot weather london" in some chat
  bot <  [inline results] Default inline handler
user  >  picks the "Search: cats" result out of the inline list
  bot <  (silence - nothing sent to the chat)
user  >  types "just chatting"
  bot <  (silence - nothing sent to the chat)
```

### What each turn proves

| User does | Bot sends | Surface demonstrated |
| --- | --- | --- |
| types "/start" | *Vexel.Telegram v2 sample*  Button-first menu. Tap a row, or use a slash command.  • *Ping* - callback route • *Sign up* - multi-turn `Flow.PromptAsync` • *Colors* - callback with a bound suffix • *Inline tip* - try `@bot search cats` • *Help* - what each piece demonstrates  Commands always win over an armed flow, so `/menu` and `/cancel` work mid-signup. An unknown `/foo` mid-flow is a command miss (falls through to `On*`) - not free text. [ Ping ] [ Sign up ] [ Colors ] [ Help ] [ Try inline here ] [ Try inline anywhere ] | `[Handler]` + `[Command("start")]` sends the menu keyboard the sample builds |
| types "/ping" | Pong! | empty request record: `[Command("ping")]` with no arguments |
| types "/echo hi there" | You said: hi there | single-string request binds the rest of the command line |
| types "/help" | *What this sample shows*  `[Handler]` + a Vexel route or `[On*]` attribute on every type (Immediate.Apis parity).  • `/start`, `/menu` - button-first home keyboard • `/ping`, `/echo hi`, `/help`, `/inline`, `/signup` • Callbacks: `ping`, `signup`, `colors`, `pick_color\|red`, `menu`, `help` • Flow: `/signup` or the Sign up button → name → age (`Flow.PromptAsync`) • Inline: `@bot search cats` and bare `@bot ...` • `[OnMessage]` observer logs every message after the routed leg  Built-in `/cancel` clears an armed flow (unless you define your own `[Command("cancel")]`).  Wiring in `Program.cs`: `AddTelegramBot` + `AddVexelTelegramSampleHandlers` + `AddVexelTelegramSampleTelegram`. | the help command every sample surface is listed in |
| types "/inline" | *Inline mode*  Type `@your_bot search kittens` in any chat, or tap a button below.  Empty / unmatched queries hit the default `[InlineQuery]` handler. Picking a result routes `[ChosenInlineResult("item")]`. [ Search here ] [ Search anywhere ] | switch-inline buttons the keyboard builder emits, for trying inline mode |
| taps [Ping] (callback_data "ping") | Pong!<br>Pong from a `[Callback]` handler at 22:15:43 UTC. [ Ping ] [ Sign up ] [ Colors ] [ Help ] [ Try inline here ] [ Try inline anywhere ] | `[Callback("ping")]`: alert answer plus an edit of the bot's own message |
| taps [Colors] (callback_data "colors") | (button spinner stops, no popup)<br>Pick a color. The suffix after `\|` binds to the handler. [ Red ] [ Green ] [ Blue ] [ Back ] | callback edits the message into a second keyboard whose data carries suffixes |
| taps [Red] (callback_data "pick_color\|red") | Selected red<br>chat100 picked *red*. [ Pick again ] [ Menu ] | `[Callback("pick_color")]` binds the suffix after `\|` to the request string |
| types "/signup" | What's your name?  (Commands still win mid-flow: try /cancel. An unknown /foo is not free text.) | command arms the first flow step with `flow.PromptAsync<CollectName.Command>()` |
| types "Ada Lovelace" | Nice to meet you, Ada Lovelace. How old are you? (send a number) | plain text goes to the armed step, which saves a draft and arms the next one |
| types "/menu" | *Vexel.Telegram v2 sample*  Button-first menu. Tap a row, or use a slash command.  • *Ping* - callback route • *Sign up* - multi-turn `Flow.PromptAsync` • *Colors* - callback with a bound suffix • *Inline tip* - try `@bot search cats` • *Help* - what each piece demonstrates  Commands always win over an armed flow, so `/menu` and `/cancel` work mid-signup. An unknown `/foo` mid-flow is a command miss (falls through to `On*`) - not free text. [ Ping ] [ Sign up ] [ Colors ] [ Help ] [ Try inline here ] [ Try inline anywhere ] | commands still win over an armed step, so /menu works mid-signup |
| types "twelve-ish" | Something went wrong - try again, or /cancel. | the step threw: generic retry line, and the step stays armed (B3) |
| types "36" | Registered Ada Lovelace, age 36. Flow complete.  /menu to go home. | retry succeeds, the draft survived the throw, and the flow auto-completes |
| taps [Sign up] (callback_data "signup") | (button spinner stops, no popup)<br>Starting signup…<br>What's your name?  (Commands still win mid-flow: try /cancel. An unknown /foo is not free text.) | the same flow starts from a callback button |
| types "/nope" | _(silence)_ | the README pitfall: an unknown command mid-flow is a command miss, never step input |
| types "/cancel" | Cancelled. | built-in /cancel clears the armed step (the sample defines no /cancel of its own) |
| types "Ada again" | _(silence)_ | nothing armed after cancel, so plain text is unrouted and the bot stays quiet |
| types "@vexel_bot search cats" in some chat | Search: cats \| Docs hit for cats | `[InlineQuery("search")]`: first token routes, the remainder binds as the term |
| types "@vexel_bot weather london" in some chat | Default inline handler | unmatched first token falls back to the empty-trigger `[InlineQuery]` default |
| picks the "Search: cats" result out of the inline list | _(silence)_ | `[ChosenInlineResult("item")]` binds the suffix after `\|` and logs the pick |
| types "just chatting" | _(silence)_ | no route matches, so only the `[OnMessage]` observer runs - it logs, it does not reply |

## 3. Command menu the sample publishes at start (`setMyCommands`)

| Command | Description |
| --- | --- |
| `/echo` | Echo the rest of the message |
| `/help` | Explain the sample surfaces |
| `/inline` | Show how to try inline mode |
| `/menu` | Open the sample menu |
| `/ping` | Check that the bot is alive |
| `/signup` | Two-step registration flow demo |
| `/start` | Open the sample menu |

## 4. `[On*]` observers (they log, they never reply)

```text
[ 1.636s] Information Command          OnMessage after routed leg: chat=100 user=100 text=/start
[ 2.136s] Information Command          OnMessage after routed leg: chat=100 user=100 text=/ping
[ 2.463s] Information Command          OnMessage after routed leg: chat=100 user=100 text=/echo hi there
[ 2.761s] Information Command          OnMessage after routed leg: chat=100 user=100 text=/help
[ 3.099s] Information Command          OnMessage after routed leg: chat=100 user=100 text=/inline
[ 3.671s] Information Command          OnCallbackQuery after routed leg: data=ping from=100
[ 4.142s] Information Command          OnCallbackQuery after routed leg: data=colors from=100
[ 4.702s] Information Command          OnCallbackQuery after routed leg: data=pick_color|red from=100
[ 4.987s] Information Command          OnMessage after routed leg: chat=100 user=100 text=/signup
[ 5.313s] Information Command          OnMessage after routed leg: chat=100 user=100 text=Ada Lovelace
[ 5.627s] Information Command          OnMessage after routed leg: chat=100 user=100 text=/menu
[ 5.952s] Information Command          OnMessage after routed leg: chat=100 user=100 text=twelve-ish
[ 6.256s] Information Command          OnMessage after routed leg: chat=100 user=100 text=36
[ 6.928s] Information Command          OnCallbackQuery after routed leg: data=signup from=100
[ 7.099s] Information Command          OnMessage after routed leg: chat=100 user=100 text=/nope
[ 8.094s] Information Command          OnMessage after routed leg: chat=100 user=100 text=/cancel
[ 8.255s] Information Command          OnMessage after routed leg: chat=100 user=100 text=Ada again
[ 9.170s] Information Command          OnInlineQuery after routed leg: query=search cats from=100
[ 9.716s] Information Command          OnInlineQuery after routed leg: query=weather london from=100
[ 9.912s] Information Command          Chosen inline result from 100: term=cats, resultId=item|cats, inlineMessageId=inline-msg-1
[10.539s] Information Command          OnMessage after routed leg: chat=100 user=100 text=just chatting
```

## 5. Route table the Vexel generator emitted for the sample (`Vexel.Telegram.Routes.g.cs`)

```csharp
// <auto-generated />
#nullable enable

#pragma warning disable CS1591

namespace Vexel.Telegram.Sample;

/// <summary>Generated Vexel Telegram route registration.</summary>
[global::System.CodeDom.Compiler.GeneratedCodeAttribute("Vexel.Telegram.Generators", "2.0.0")]
public static partial class TelegramServiceCollectionExtensions
{
	/// <summary>Registers Telegram routes discovered in this assembly (VexelTelegramSample).</summary>
	public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddVexelTelegramSampleTelegram(
		this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
	{
		global::System.ArgumentNullException.ThrowIfNull(services);

		var commands = new global::System.Collections.Generic.Dictionary<string, global::Vexel.Telegram.Handlers.Routing.RouteBinder>(
			global::System.StringComparer.OrdinalIgnoreCase)
		{
			["echo"] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.Echo.Handler>(scope);
				_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.Echo.Command(payload), cancellationToken).ConfigureAwait(false);
				return true;
			},
			["help"] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.Help.Handler>(scope);
				_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.Help.Command(), cancellationToken).ConfigureAwait(false);
				return true;
			},
			["inline"] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.InlineTip.Handler>(scope);
				_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.InlineTip.Command(), cancellationToken).ConfigureAwait(false);
				return true;
			},
			["menu"] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.Menu.Handler>(scope);
				_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.Menu.Command(), cancellationToken).ConfigureAwait(false);
				return true;
			},
			["ping"] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.Ping.Handler>(scope);
				_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.Ping.Command(), cancellationToken).ConfigureAwait(false);
				return true;
			},
			["signup"] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.Signup.Handler>(scope);
				_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.Signup.Command(), cancellationToken).ConfigureAwait(false);
				return true;
			},
			["start"] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.Start.Handler>(scope);
				_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.Start.Command(), cancellationToken).ConfigureAwait(false);
				return true;
			},
		};

		var metadata = new global::Vexel.Telegram.Handlers.Routing.CommandRouteMetadata[]
		{
			new global::Vexel.Telegram.Handlers.Routing.CommandRouteMetadata("echo", "Echo the rest of the message"),
			new global::Vexel.Telegram.Handlers.Routing.CommandRouteMetadata("help", "Explain the sample surfaces"),
			new global::Vexel.Telegram.Handlers.Routing.CommandRouteMetadata("inline", "Show how to try inline mode"),
			new global::Vexel.Telegram.Handlers.Routing.CommandRouteMetadata("menu", "Open the sample menu"),
			new global::Vexel.Telegram.Handlers.Routing.CommandRouteMetadata("ping", "Check that the bot is alive"),
			new global::Vexel.Telegram.Handlers.Routing.CommandRouteMetadata("signup", "Two-step registration flow demo"),
			new global::Vexel.Telegram.Handlers.Routing.CommandRouteMetadata("start", "Open the sample menu"),
		};

		var callbacks = new global::System.Collections.Generic.Dictionary<string, global::Vexel.Telegram.Handlers.Routing.RouteBinder>(
			global::System.StringComparer.Ordinal)
		{
			["colors"] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.ColorsCallback.Handler>(scope);
				_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.ColorsCallback.Command(), cancellationToken).ConfigureAwait(false);
				return true;
			},
			["help"] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.HelpCallback.Handler>(scope);
				_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.HelpCallback.Command(), cancellationToken).ConfigureAwait(false);
				return true;
			},
			["menu"] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.MenuCallback.Handler>(scope);
				_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.MenuCallback.Command(), cancellationToken).ConfigureAwait(false);
				return true;
			},
			["pick_color"] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.PickColor.Handler>(scope);
				_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.PickColor.Command(payload), cancellationToken).ConfigureAwait(false);
				return true;
			},
			["ping"] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.PingCallback.Handler>(scope);
				_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.PingCallback.Command(), cancellationToken).ConfigureAwait(false);
				return true;
			},
			["signup"] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.SignupCallback.Handler>(scope);
				_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.SignupCallback.Command(), cancellationToken).ConfigureAwait(false);
				return true;
			},
		};

		var inlineQueries = new global::System.Collections.Generic.Dictionary<string, global::Vexel.Telegram.Handlers.Routing.RouteBinder>(
			global::System.StringComparer.Ordinal)
		{
			[""] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.DefaultInline.Handler>(scope);
				_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.DefaultInline.Query(payload), cancellationToken).ConfigureAwait(false);
				return true;
			},
			["search"] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.SearchInline.Handler>(scope);
				_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.SearchInline.Query(payload), cancellationToken).ConfigureAwait(false);
				return true;
			},
		};

		var chosenInlineResults = new global::System.Collections.Generic.Dictionary<string, global::Vexel.Telegram.Handlers.Routing.RouteBinder>(
			global::System.StringComparer.Ordinal)
		{
			["item"] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.ItemChosen.Handler>(scope);
				_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.ItemChosen.Command(payload), cancellationToken).ConfigureAwait(false);
				return true;
			},
		};

		var flowSteps = new global::System.Collections.Generic.Dictionary<string, global::Vexel.Telegram.Handlers.Routing.RouteBinder>(
			global::System.StringComparer.Ordinal)
		{
			[typeof(global::Vexel.Telegram.Sample.Handlers.CollectAge.Command).FullName!] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.CollectAge.Handler>(scope);
				_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.CollectAge.Command(payload), cancellationToken).ConfigureAwait(false);
				return true;
			},
			[typeof(global::Vexel.Telegram.Sample.Handlers.CollectName.Command).FullName!] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.CollectName.Handler>(scope);
				_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.CollectName.Command(payload), cancellationToken).ConfigureAwait(false);
				return true;
			},
		};

		var onMessages = new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry[]
		{
			new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry(
				"global::Vexel.Telegram.Sample.Handlers.MessageAudit",
				static async (scope, payload, cancellationToken) =>
				{
					var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.MessageAudit.Handler>(scope);
					_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.MessageAudit.Command(), cancellationToken).ConfigureAwait(false);
					return true;
				}),
		};

		var onCallbackQueries = new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry[]
		{
			new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry(
				"global::Vexel.Telegram.Sample.Handlers.CallbackAudit",
				static async (scope, payload, cancellationToken) =>
				{
					var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.CallbackAudit.Handler>(scope);
					_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.CallbackAudit.Command(), cancellationToken).ConfigureAwait(false);
					return true;
				}),
		};

		var onInlineQueries = new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry[]
		{
			new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry(
				"global::Vexel.Telegram.Sample.Handlers.InlineAudit",
				static async (scope, payload, cancellationToken) =>
				{
					var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Vexel.Telegram.Sample.Handlers.InlineAudit.Handler>(scope);
					_ = await handler.HandleAsync(new global::Vexel.Telegram.Sample.Handlers.InlineAudit.Command(), cancellationToken).ConfigureAwait(false);
					return true;
				}),
		};

		var onChosenInlineResults = new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry[]
		{
		};

		_ = global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(
			services,
			new global::Vexel.Telegram.Handlers.Routing.TelegramRouteContribution(
				"VexelTelegramSample",
				commands,
				metadata,
				callbacks,
				inlineQueries,
				chosenInlineResults,
				flowSteps,
				onMessages,
				onCallbackQueries,
				onInlineQueries,
				onChosenInlineResults));

		return services;
	}
}
```

## 6. Bot -> Telegram Bot API calls, in order

```text
setMyCommands commands=[/echo - "Echo the rest of the message"; /help - "Explain the sample surfaces"; /inline - "Show how to try inline mode"; /menu - "Open the sample menu"; /ping - "Check that the bot is alive"; /signup - "Two-step registration flow demo"; /start - "Open the sample menu"]
deleteWebhook drop_pending_updates=true
sendMessage chat_id=100 text="*Vexel.Telegram v2 sample*

Button-first menu. Tap a row, or use a slash command.

• *Ping* - callback route
• *Sign up* - multi-turn `Flow.PromptAsync`
• *Colors* - callback with a bound suffix
• *Inline tip* - try `@bot search cats`
• *Help* - what each piece demonstrates

Commands always win over an armed flow, so `/menu` and `/cancel` work mid-signup.
An unknown `/foo` mid-flow is a command miss (falls through to `On*`) - not free text." reply_markup=<inline keyboard> buttons=["Ping" -> callback_data="ping"; "Sign up" -> callback_data="signup"; "Colors" -> callback_data="colors"; "Help" -> callback_data="help"; "Try inline here" -> <other>; "Try inline anywhere" -> switch_inline_query="search "]
sendMessage chat_id=100 text="Pong!"
sendMessage chat_id=100 text="You said: hi there"
sendMessage chat_id=100 text="*What this sample shows*

`[Handler]` + a Vexel route or `[On*]` attribute on every type (Immediate.Apis parity).

• `/start`, `/menu` - button-first home keyboard
• `/ping`, `/echo hi`, `/help`, `/inline`, `/signup`
• Callbacks: `ping`, `signup`, `colors`, `pick_color|red`, `menu`, `help`
• Flow: `/signup` or the Sign up button → name → age (`Flow.PromptAsync`)
• Inline: `@bot search cats` and bare `@bot ...`
• `[OnMessage]` observer logs every message after the routed leg

Built-in `/cancel` clears an armed flow (unless you define your own `[Command("cancel")]`).

Wiring in `Program.cs`:
`AddTelegramBot` + `AddVexelTelegramSampleHandlers` + `AddVexelTelegramSampleTelegram`."
sendMessage chat_id=100 text="*Inline mode*

Type `@your_bot search kittens` in any chat, or tap a button below.

Empty / unmatched queries hit the default `[InlineQuery]` handler.
Picking a result routes `[ChosenInlineResult("item")]`." reply_markup=<inline keyboard> buttons=["Search here" -> <other>; "Search anywhere" -> switch_inline_query="search kittens"]
answerCallbackQuery callback_query_id="cb-706" text="Pong!" show_alert=true
editMessageText chat_id=100 message_id=9001 text="Pong from a `[Callback]` handler at 22:15:43 UTC." reply_markup=<inline keyboard> buttons=["Ping" -> callback_data="ping"; "Sign up" -> callback_data="signup"; "Colors" -> callback_data="colors"; "Help" -> callback_data="help"; "Try inline here" -> <other>; "Try inline anywhere" -> switch_inline_query="search "]
answerCallbackQuery callback_query_id="cb-707"
editMessageText chat_id=100 message_id=9001 text="Pick a color. The suffix after `|` binds to the handler." reply_markup=<inline keyboard> buttons=["Red" -> callback_data="pick_color|red"; "Green" -> callback_data="pick_color|green"; "Blue" -> callback_data="pick_color|blue"; "Back" -> callback_data="menu"]
answerCallbackQuery callback_query_id="cb-708" text="Selected red"
editMessageText chat_id=100 message_id=9001 text="chat100 picked *red*." reply_markup=<inline keyboard> buttons=["Pick again" -> callback_data="colors"; "Menu" -> callback_data="menu"]
sendMessage chat_id=100 text="What's your name?

(Commands still win mid-flow: try /cancel. An unknown /foo is not free text.)"
sendMessage chat_id=100 text="Nice to meet you, Ada Lovelace. How old are you? (send a number)"
sendMessage chat_id=100 text="*Vexel.Telegram v2 sample*

Button-first menu. Tap a row, or use a slash command.

• *Ping* - callback route
• *Sign up* - multi-turn `Flow.PromptAsync`
• *Colors* - callback with a bound suffix
• *Inline tip* - try `@bot search cats`
• *Help* - what each piece demonstrates

Commands always win over an armed flow, so `/menu` and `/cancel` work mid-signup.
An unknown `/foo` mid-flow is a command miss (falls through to `On*`) - not free text." reply_markup=<inline keyboard> buttons=["Ping" -> callback_data="ping"; "Sign up" -> callback_data="signup"; "Colors" -> callback_data="colors"; "Help" -> callback_data="help"; "Try inline here" -> <other>; "Try inline anywhere" -> switch_inline_query="search "]
sendMessage chat_id=100 text="Something went wrong - try again, or /cancel."
sendMessage chat_id=100 text="Registered Ada Lovelace, age 36. Flow complete.

/menu to go home."
answerCallbackQuery callback_query_id="cb-714"
editMessageText chat_id=100 message_id=9001 text="Starting signup…"
sendMessage chat_id=100 text="What's your name?

(Commands still win mid-flow: try /cancel. An unknown /foo is not free text.)"
sendMessage chat_id=100 text="Cancelled."
answerInlineQuery inline_query_id="iq-718" cache_time=0 results=[item|cats -> "Search: cats"; item|cats-docs -> "Docs hit for cats"]
answerInlineQuery inline_query_id="iq-719" cache_time=0 results=[item|default -> "Default inline handler"]
```

## 7. Live session log

```text
[ 0.805s] compiled the sample's own handler files (Commands.cs, Inline.cs, MarkdownText.cs, Menu.cs, Observers.cs, SignupFlow.cs) with Immediate + Vexel generators
[ 0.818s] fake Telegram Bot API listening on http://127.0.0.1:52557 (bot username @vexel_bot)
[ 0.859s] telegram-api  <- bot calls setMyCommands commands=[/echo - "Echo the rest of the message"; /help - "Explain the sample surfaces"; /inline - "Show how to try inline mode"; /menu - "Open the sample menu"; /ping - "Check that the bot is alive"; /signup - "Two-step registration flow demo"; /start - "Open the sample menu"]
[ 0.864s] Information SetMyCommandsInitializer Registered 7 bot command(s) with Telegram (setMyCommands).
[ 0.865s] Information Lifetime         Application started. Press Ctrl+C to shut down.
[ 0.865s] Information Lifetime         Hosting environment: Production
[ 0.865s] Information Lifetime         Content root path: /Users/shiki/.no-mistakes/worktrees/b6147a1cfc26/01KZ7DC95BBE2Q3HJ5CN6P6WQY/tests/Vexel.Telegram.Tests/bin/Release/net10.0/
[ 0.866s] telegram-api  <- bot calls deleteWebhook drop_pending_updates=true
[ 0.866s] Information VexelClient      VexelClient starting polling (DropPendingUpdates=True, LaneCapacity=64)
[ 0.872s] telegram-api  getUpdates(offset=-1) -> no updates
[ 1.059s] telegram-api  getUpdates(offset=0) -> no updates
[ 1.229s] telegram-api  getUpdates(offset=0) -> no updates
[ 1.230s] user types "/start"
[ 1.396s] telegram-api  getUpdates(offset=0) -> update 701
[ 1.407s] telegram-api  getUpdates(offset=702) -> no updates
[ 1.625s] telegram-api  <- bot calls sendMessage chat_id=100 text="*Vexel.Telegram v2 sample*

Button-first menu. Tap a row, or use a slash command.

• *Ping* - callback route
• *Sign up* - multi-turn `Flow.PromptAsync`
• *Colors* - callback with a bound suffix
• *Inline tip* - try `@bot search cats`
• *Help* - what each piece demonstrates

Commands always win over an armed flow, so `/menu` and `/cancel` work mid-signup.
An unknown `/foo` mid-flow is a command miss (falls through to `On*`) - not free text." reply_markup=<inline keyboard> buttons=["Ping" -> callback_data="ping"; "Sign up" -> callback_data="signup"; "Colors" -> callback_data="colors"; "Help" -> callback_data="help"; "Try inline here" -> <other>; "Try inline anywhere" -> switch_inline_query="search "]
[ 1.627s] telegram-api  getUpdates(offset=702) -> no updates
[ 1.636s] Information Command          OnMessage after routed leg: chat=100 user=100 text=/start
[ 1.790s] telegram-api  getUpdates(offset=702) -> no updates
[ 1.800s] user types "/ping"
[ 1.967s] telegram-api  getUpdates(offset=702) -> update 702
[ 1.968s] telegram-api  getUpdates(offset=703) -> no updates
[ 2.134s] telegram-api  <- bot calls sendMessage chat_id=100 text="Pong!"
[ 2.134s] telegram-api  getUpdates(offset=703) -> no updates
[ 2.136s] Information Command          OnMessage after routed leg: chat=100 user=100 text=/ping
[ 2.302s] user types "/echo hi there"
[ 2.303s] telegram-api  getUpdates(offset=703) -> update 703
[ 2.304s] telegram-api  getUpdates(offset=704) -> no updates
[ 2.457s] telegram-api  <- bot calls sendMessage chat_id=100 text="You said: hi there"
[ 2.458s] telegram-api  getUpdates(offset=704) -> no updates
[ 2.463s] Information Command          OnMessage after routed leg: chat=100 user=100 text=/echo hi there
[ 2.552s] user types "/help"
[ 2.596s] telegram-api  getUpdates(offset=704) -> update 704
[ 2.597s] telegram-api  getUpdates(offset=705) -> no updates
[ 2.759s] telegram-api  <- bot calls sendMessage chat_id=100 text="*What this sample shows*

`[Handler]` + a Vexel route or `[On*]` attribute on every type (Immediate.Apis parity).

• `/start`, `/menu` - button-first home keyboard
• `/ping`, `/echo hi`, `/help`, `/inline`, `/signup`
• Callbacks: `ping`, `signup`, `colors`, `pick_color|red`, `menu`, `help`
• Flow: `/signup` or the Sign up button → name → age (`Flow.PromptAsync`)
• Inline: `@bot search cats` and bare `@bot ...`
• `[OnMessage]` observer logs every message after the routed leg

Built-in `/cancel` clears an armed flow (unless you define your own `[Command("cancel")]`).

Wiring in `Program.cs`:
`AddTelegramBot` + `AddVexelTelegramSampleHandlers` + `AddVexelTelegramSampleTelegram`."
[ 2.760s] telegram-api  getUpdates(offset=705) -> no updates
[ 2.761s] Information Command          OnMessage after routed leg: chat=100 user=100 text=/help
[ 2.929s] user types "/inline"
[ 2.930s] telegram-api  getUpdates(offset=705) -> update 705
[ 2.931s] telegram-api  getUpdates(offset=706) -> no updates
[ 3.095s] telegram-api  <- bot calls sendMessage chat_id=100 text="*Inline mode*

Type `@your_bot search kittens` in any chat, or tap a button below.

Empty / unmatched queries hit the default `[InlineQuery]` handler.
Picking a result routes `[ChosenInlineResult("item")]`." reply_markup=<inline keyboard> buttons=["Search here" -> <other>; "Search anywhere" -> switch_inline_query="search kittens"]
[ 3.099s] Information Command          OnMessage after routed leg: chat=100 user=100 text=/inline
[ 3.099s] telegram-api  getUpdates(offset=706) -> no updates
[ 3.225s] user taps [Ping] (callback_data "ping")
[ 3.227s] telegram-api  getUpdates(offset=706) -> no updates
[ 3.393s] telegram-api  getUpdates(offset=706) -> update 706
[ 3.397s] telegram-api  getUpdates(offset=707) -> no updates
[ 3.504s] telegram-api  <- bot calls answerCallbackQuery callback_query_id="cb-706" text="Pong!" show_alert=true
[ 3.505s] telegram-api  getUpdates(offset=707) -> no updates
[ 3.661s] telegram-api  <- bot calls editMessageText chat_id=100 message_id=9001 text="Pong from a `[Callback]` handler at 22:15:43 UTC." reply_markup=<inline keyboard> buttons=["Ping" -> callback_data="ping"; "Sign up" -> callback_data="signup"; "Colors" -> callback_data="colors"; "Help" -> callback_data="help"; "Try inline here" -> <other>; "Try inline anywhere" -> switch_inline_query="search "]
[ 3.663s] telegram-api  getUpdates(offset=707) -> no updates
[ 3.671s] Information Command          OnCallbackQuery after routed leg: data=ping from=100
[ 3.814s] user taps [Colors] (callback_data "colors")
[ 3.815s] telegram-api  getUpdates(offset=707) -> update 707
[ 3.817s] telegram-api  getUpdates(offset=708) -> no updates
[ 3.977s] telegram-api  <- bot calls answerCallbackQuery callback_query_id="cb-707"
[ 3.978s] telegram-api  getUpdates(offset=708) -> no updates
[ 4.140s] telegram-api  <- bot calls editMessageText chat_id=100 message_id=9001 text="Pick a color. The suffix after `|` binds to the handler." reply_markup=<inline keyboard> buttons=["Red" -> callback_data="pick_color|red"; "Green" -> callback_data="pick_color|green"; "Blue" -> callback_data="pick_color|blue"; "Back" -> callback_data="menu"]
[ 4.141s] telegram-api  getUpdates(offset=708) -> no updates
[ 4.142s] Information Command          OnCallbackQuery after routed leg: data=colors from=100
[ 4.200s] user taps [Red] (callback_data "pick_color|red")
[ 4.373s] telegram-api  getUpdates(offset=708) -> update 708
[ 4.374s] telegram-api  getUpdates(offset=709) -> no updates
[ 4.518s] telegram-api  <- bot calls answerCallbackQuery callback_query_id="cb-708" text="Selected red"
[ 4.518s] telegram-api  getUpdates(offset=709) -> no updates
[ 4.701s] telegram-api  <- bot calls editMessageText chat_id=100 message_id=9001 text="chat100 picked *red*." reply_markup=<inline keyboard> buttons=["Pick again" -> callback_data="colors"; "Menu" -> callback_data="menu"]
[ 4.701s] telegram-api  getUpdates(offset=709) -> no updates
[ 4.702s] Information Command          OnCallbackQuery after routed leg: data=pick_color|red from=100
[ 4.835s] user types "/signup"
[ 4.842s] telegram-api  getUpdates(offset=709) -> update 709
[ 4.843s] telegram-api  getUpdates(offset=710) -> no updates
[ 4.986s] telegram-api  <- bot calls sendMessage chat_id=100 text="What's your name?

(Commands still win mid-flow: try /cancel. An unknown /foo is not free text.)"
[ 4.987s] telegram-api  getUpdates(offset=710) -> no updates
[ 4.987s] Information Command          OnMessage after routed leg: chat=100 user=100 text=/signup
[ 5.147s] user types "Ada Lovelace"
[ 5.148s] telegram-api  getUpdates(offset=710) -> update 710
[ 5.149s] telegram-api  getUpdates(offset=711) -> no updates
[ 5.310s] telegram-api  <- bot calls sendMessage chat_id=100 text="Nice to meet you, Ada Lovelace. How old are you? (send a number)"
[ 5.311s] telegram-api  getUpdates(offset=711) -> no updates
[ 5.313s] Information Command          OnMessage after routed leg: chat=100 user=100 text=Ada Lovelace
[ 5.400s] user types "/menu"
[ 5.481s] telegram-api  getUpdates(offset=711) -> update 711
[ 5.483s] telegram-api  getUpdates(offset=712) -> no updates
[ 5.627s] telegram-api  <- bot calls sendMessage chat_id=100 text="*Vexel.Telegram v2 sample*

Button-first menu. Tap a row, or use a slash command.

• *Ping* - callback route
• *Sign up* - multi-turn `Flow.PromptAsync`
• *Colors* - callback with a bound suffix
• *Inline tip* - try `@bot search cats`
• *Help* - what each piece demonstrates

Commands always win over an armed flow, so `/menu` and `/cancel` work mid-signup.
An unknown `/foo` mid-flow is a command miss (falls through to `On*`) - not free text." reply_markup=<inline keyboard> buttons=["Ping" -> callback_data="ping"; "Sign up" -> callback_data="signup"; "Colors" -> callback_data="colors"; "Help" -> callback_data="help"; "Try inline here" -> <other>; "Try inline anywhere" -> switch_inline_query="search "]
[ 5.627s] Information Command          OnMessage after routed leg: chat=100 user=100 text=/menu
[ 5.627s] telegram-api  getUpdates(offset=712) -> no updates
[ 5.789s] user types "twelve-ish"
[ 5.790s] telegram-api  getUpdates(offset=712) -> update 712
[ 5.790s] telegram-api  getUpdates(offset=713) -> no updates
[ 5.791s] Error       TelegramRouter   Flow step 'Vexel.Telegram.Sample.Handlers.CollectAge+Command' failed for update 712 (FormatException: age was not a sensible number)
[ 5.950s] telegram-api  <- bot calls sendMessage chat_id=100 text="Something went wrong - try again, or /cancel."
[ 5.952s] telegram-api  getUpdates(offset=713) -> no updates
[ 5.952s] Information Command          OnMessage after routed leg: chat=100 user=100 text=twelve-ish
[ 6.024s] user types "36"
[ 6.096s] telegram-api  getUpdates(offset=713) -> update 713
[ 6.097s] telegram-api  getUpdates(offset=714) -> no updates
[ 6.254s] telegram-api  <- bot calls sendMessage chat_id=100 text="Registered Ada Lovelace, age 36. Flow complete.

/menu to go home."
[ 6.255s] telegram-api  getUpdates(offset=714) -> no updates
[ 6.256s] Information Command          OnMessage after routed leg: chat=100 user=100 text=36
[ 6.413s] user taps [Sign up] (callback_data "signup")
[ 6.415s] telegram-api  getUpdates(offset=714) -> update 714
[ 6.416s] telegram-api  getUpdates(offset=715) -> no updates
[ 6.578s] telegram-api  <- bot calls answerCallbackQuery callback_query_id="cb-714"
[ 6.578s] telegram-api  getUpdates(offset=715) -> no updates
[ 6.761s] telegram-api  <- bot calls editMessageText chat_id=100 message_id=9001 text="Starting signup…"
[ 6.762s] telegram-api  getUpdates(offset=715) -> no updates
[ 6.927s] telegram-api  <- bot calls sendMessage chat_id=100 text="What's your name?

(Commands still win mid-flow: try /cancel. An unknown /foo is not free text.)"
[ 6.928s] telegram-api  getUpdates(offset=715) -> no updates
[ 6.928s] Information Command          OnCallbackQuery after routed leg: data=signup from=100
[ 7.097s] user types "/nope"
[ 7.098s] telegram-api  getUpdates(offset=715) -> update 715
[ 7.099s] Information Command          OnMessage after routed leg: chat=100 user=100 text=/nope
[ 7.099s] telegram-api  getUpdates(offset=716) -> no updates
[ 7.265s] telegram-api  getUpdates(offset=716) -> no updates
[ 7.505s] telegram-api  getUpdates(offset=716) -> no updates
[ 7.726s] telegram-api  getUpdates(offset=716) -> no updates
[ 7.727s] user types "/cancel"
[ 7.883s] telegram-api  getUpdates(offset=716) -> update 716
[ 7.883s] telegram-api  getUpdates(offset=717) -> no updates
[ 8.091s] telegram-api  <- bot calls sendMessage chat_id=100 text="Cancelled."
[ 8.092s] telegram-api  getUpdates(offset=717) -> no updates
[ 8.094s] Information Command          OnMessage after routed leg: chat=100 user=100 text=/cancel
[ 8.254s] user types "Ada again"
[ 8.255s] telegram-api  getUpdates(offset=717) -> update 717
[ 8.255s] Information Command          OnMessage after routed leg: chat=100 user=100 text=Ada again
[ 8.256s] telegram-api  getUpdates(offset=718) -> no updates
[ 8.425s] telegram-api  getUpdates(offset=718) -> no updates
[ 8.659s] telegram-api  getUpdates(offset=718) -> no updates
[ 8.823s] telegram-api  getUpdates(offset=718) -> no updates
[ 8.839s] user types "@vexel_bot search cats" in some chat
[ 8.998s] telegram-api  getUpdates(offset=718) -> update 718
[ 9.001s] telegram-api  getUpdates(offset=719) -> no updates
[ 9.162s] telegram-api  <- bot calls answerInlineQuery inline_query_id="iq-718" cache_time=0 results=[item|cats -> "Search: cats"; item|cats-docs -> "Docs hit for cats"]
[ 9.163s] telegram-api  getUpdates(offset=719) -> no updates
[ 9.170s] Information Command          OnInlineQuery after routed leg: query=search cats from=100
[ 9.324s] user types "@vexel_bot weather london" in some chat
[ 9.324s] telegram-api  getUpdates(offset=719) -> no updates
[ 9.548s] telegram-api  getUpdates(offset=719) -> update 719
[ 9.549s] telegram-api  getUpdates(offset=720) -> no updates
[ 9.716s] telegram-api  <- bot calls answerInlineQuery inline_query_id="iq-719" cache_time=0 results=[item|default -> "Default inline handler"]
[ 9.716s] telegram-api  getUpdates(offset=720) -> no updates
[ 9.716s] Information Command          OnInlineQuery after routed leg: query=weather london from=100
[ 9.737s] user picks the "Search: cats" result out of the inline list
[ 9.904s] telegram-api  getUpdates(offset=720) -> update 720
[ 9.906s] telegram-api  getUpdates(offset=721) -> no updates
[ 9.912s] Information Command          Chosen inline result from 100: term=cats, resultId=item|cats, inlineMessageId=inline-msg-1
[10.059s] telegram-api  getUpdates(offset=721) -> no updates
[10.308s] telegram-api  getUpdates(offset=721) -> no updates
[10.537s] user types "just chatting"
[10.538s] telegram-api  getUpdates(offset=721) -> update 721
[10.539s] Information Command          OnMessage after routed leg: chat=100 user=100 text=just chatting
[10.539s] telegram-api  getUpdates(offset=722) -> no updates
[10.708s] telegram-api  getUpdates(offset=722) -> no updates
[10.947s] telegram-api  getUpdates(offset=722) -> no updates
[11.194s] telegram-api  getUpdates(offset=722) -> no updates
[11.194s] Information Lifetime         Application is shutting down...
[11.206s] Information VexelClient      VexelClient stopped
[11.208s] host stopped
```

