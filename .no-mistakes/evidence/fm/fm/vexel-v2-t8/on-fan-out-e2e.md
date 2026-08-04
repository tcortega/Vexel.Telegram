# `[On*]` fan-out, end to end

Two bot assemblies - the app (`DemoBot`) and a handler library it consumes (`PluginBot`) -
are compiled with the Immediate and Vexel generators, loaded, registered through their
generated `Add{Assembly}Telegram()` (app first, plugin second), and run in a real host
against a fake Telegram Bot API server over HTTP.

`Watchers.MikeAudit` comes from the *second* registered assembly and still runs between the
app's own `Watchers.AlphaAudit` and `Watchers.ZuluAudit`: dispatch order is the observers'
fully-qualified metadata name, not registration order.

## 1. What the bot author wrote

### App assembly `DemoBot`

```csharp
using System.Threading;
using System.Threading.Tasks;
using Immediate.Handlers.Shared;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types.InlineQueryResults;
using Vexel.Telegram.Handlers;
using Vexel.Telegram.Handlers.Attributes;
using Vexel.Telegram.Handlers.Contexts;

namespace DemoBot
{
	/// <summary>The app's composition root: Immediate handlers + the generated Vexel routes.</summary>
	public static class BotRegistration
	{
		public static IServiceCollection AddDemoBot(IServiceCollection services) =>
			services
				.AddDemoBotHandlers()
				.AddDemoBotTelegram();
	}
}

namespace DemoBot.Routes
{
	[Handler]
	[Command("ping", Description = "Ping the bot")]
	public static partial class Ping
	{
		public sealed record Command;

		private static async ValueTask HandleAsync(
			Command command,
			Feedback feedback,
			CancellationToken token)
		{
			_ = await feedback.ReplyAsync("routed: /ping -> pong", cancellationToken: token);
		}
	}

	/// <summary>Answers nothing, so the B4 obligation has to close the callback out.</summary>
	[Handler]
	[Callback("confirm")]
	public static partial class Confirm
	{
		public sealed record Command;

		private static async ValueTask HandleAsync(
			Command command,
			Feedback feedback,
			CancellationToken token)
		{
			_ = await feedback.ReplyAsync("routed: callback confirm", cancellationToken: token);
		}
	}

	[Handler]
	[InlineQuery("search")]
	public static partial class SearchInline
	{
		public sealed record Query(string Text);

		private static async ValueTask HandleAsync(
			Query query,
			Feedback feedback,
			CancellationToken token)
		{
			await feedback.AnswerInlineAsync(
				[
					new InlineQueryResultArticle(
						$"item|{query.Text}",
						$"routed: search {query.Text}",
						new InputTextMessageContent($"You searched for {query.Text}")),
				],
				cacheTime: 30,
				cancellationToken: token);
		}
	}

	[Handler]
	[ChosenInlineResult("item")]
	public static partial class ItemChosen
	{
		public sealed record Command(string Term);

		private static async ValueTask HandleAsync(
			Command command,
			Feedback feedback,
			CancellationToken token)
		{
			await feedback.EditAsync(
				$"routed: chosen {command.Term}",
				cancellationToken: token);
		}
	}
}

namespace Watchers
{
	/// <summary>Sorts first of the three message observers.</summary>
	[Handler]
	[OnMessage]
	public static partial class AlphaAudit
	{
		public sealed record Command;

		private static async ValueTask HandleAsync(
			Command command,
			Feedback feedback,
			CancellationToken token)
		{
			_ = await feedback.ReplyAsync("on-message: Watchers.AlphaAudit", cancellationToken: token);
		}
	}

	/// <summary>Sorts last of the three message observers.</summary>
	[Handler]
	[OnMessage]
	public static partial class ZuluAudit
	{
		public sealed record Command;

		private static async ValueTask HandleAsync(
			Command command,
			Feedback feedback,
			CancellationToken token)
		{
			_ = await feedback.ReplyAsync("on-message: Watchers.ZuluAudit", cancellationToken: token);
		}
	}

	[Handler]
	[OnCallbackQuery]
	public static partial class CallbackAudit
	{
		public sealed record Command;

		private static async ValueTask HandleAsync(
			Command command,
			Feedback feedback,
			CancellationToken token)
		{
			_ = await feedback.ReplyAsync(
				"on-callback: Watchers.CallbackAudit",
				cancellationToken: token);
		}
	}

	/// <summary>
	/// Inline queries are chatless, so the observer reports by messaging the querier
	/// directly instead of answering (the routed handler owns the answer).
	/// </summary>
	[Handler]
	[OnInlineQuery]
	public static partial class InlineAudit
	{
		public sealed record Command;

		private static async ValueTask HandleAsync(
			Command command,
			InlineQueryContext context,
			ITelegramBotClient bot,
			CancellationToken token)
		{
			_ = await bot.SendMessage(
				context.UserId,
				"on-inline: Watchers.InlineAudit",
				cancellationToken: token);
		}
	}

	[Handler]
	[OnChosenInlineResult]
	public static partial class ChosenAudit
	{
		public sealed record Command;

		private static async ValueTask HandleAsync(
			Command command,
			ChosenInlineResultContext context,
			ITelegramBotClient bot,
			CancellationToken token)
		{
			_ = await bot.SendMessage(
				context.UserId,
				"on-chosen: Watchers.ChosenAudit",
				cancellationToken: token);
		}
	}
}
```

### Handler library `PluginBot`

```csharp
using System.Threading;
using System.Threading.Tasks;
using Immediate.Handlers.Shared;
using Microsoft.Extensions.DependencyInjection;
using Vexel.Telegram.Handlers;
using Vexel.Telegram.Handlers.Attributes;

namespace PluginBot
{
	public static class PluginRegistration
	{
		public static IServiceCollection AddPluginBot(IServiceCollection services) =>
			services
				.AddPluginBotHandlers()
				.AddPluginBotTelegram();
	}
}

namespace Watchers
{
	[Handler]
	[OnMessage]
	public static partial class MikeAudit
	{
		public sealed record Command;

		private static async ValueTask HandleAsync(
			Command command,
			Feedback feedback,
			CancellationToken token)
		{
			_ = await feedback.ReplyAsync("on-message: Watchers.MikeAudit", cancellationToken: token);
		}
	}
}
```

## 2. What Telegram saw, per update

### `/ping` - routed handler, then On* observers, then the raw handler

```text
sendMessage chat_id=100 text="routed: /ping -> pong"
sendMessage chat_id=100 text="on-message: Watchers.AlphaAudit"
sendMessage chat_id=100 text="on-message: Watchers.MikeAudit"
sendMessage chat_id=100 text="on-message: Watchers.ZuluAudit"
sendMessage chat_id=100 text="raw: update 901"
```

### `just chatting` - nothing routes, observers still run

```text
sendMessage chat_id=100 text="on-message: Watchers.AlphaAudit"
sendMessage chat_id=100 text="on-message: Watchers.MikeAudit"
sendMessage chat_id=100 text="on-message: Watchers.ZuluAudit"
sendMessage chat_id=100 text="raw: update 902"
```

### callback tap - routed -> On* -> raw -> fail-closed answer obligation

```text
sendMessage chat_id=100 text="routed: callback confirm"
sendMessage chat_id=100 text="on-callback: Watchers.CallbackAudit"
sendMessage chat_id=100 text="raw: update 903"
answerCallbackQuery callback_query_id="cq-903"
```

### inline query `search widgets`

```text
answerInlineQuery inline_query_id="iq-904" cache_time=30 results=[item|widgets -> "routed: search widgets"]
sendMessage chat_id=300 text="on-inline: Watchers.InlineAudit"
sendMessage chat_id=300 text="raw: update 904"
```

### chosen inline result `item|widgets`

```text
editMessageText inline_message_id="im-905" text="routed: chosen widgets"
sendMessage chat_id=300 text="on-chosen: Watchers.ChosenAudit"
sendMessage chat_id=300 text="raw: update 905"
```

## 3. Live session transcript

```text
[ 0.929s] compiled DemoBot + PluginBot with Immediate + Vexel generators
[ 0.930s] registered AddDemoBotTelegram() first, AddPluginBotTelegram() second
[ 0.940s] fake Telegram Bot API listening on http://127.0.0.1:64403 (bot username @vexel_bot)
[ 0.976s] Information Lifetime         Application started. Press Ctrl+C to shut down.
[ 0.977s] Information Lifetime         Hosting environment: Production
[ 0.977s] Information VexelClient      VexelClient starting polling (DropPendingUpdates=True, LaneCapacity=64)
[ 0.977s] Information Lifetime         Content root path: /Users/shiki/.no-mistakes/worktrees/b6147a1cfc26/01KZ744P7KTATFG14EEK1EFJFT/tests/Vexel.Telegram.Tests/bin/Debug/net10.0/
[ 1.016s] telegram-api  getUpdates(offset=-1) -> no updates
[ 1.241s] telegram-api  getUpdates(offset=0) -> no updates
[ 1.241s] user in chat 100 sends "/ping"
[ 1.342s] telegram-api  getUpdates(offset=0) -> update 901
[ 1.347s] telegram-api  getUpdates(offset=902) -> no updates
[ 1.458s] telegram-api  <- bot calls sendMessage chat_id=100 text="routed: /ping -> pong"
[ 1.459s] telegram-api  getUpdates(offset=902) -> no updates
[ 1.565s] telegram-api  <- bot calls sendMessage chat_id=100 text="on-message: Watchers.AlphaAudit"
[ 1.565s] telegram-api  getUpdates(offset=902) -> no updates
[ 1.665s] telegram-api  <- bot calls sendMessage chat_id=100 text="on-message: Watchers.MikeAudit"
[ 1.666s] telegram-api  getUpdates(offset=902) -> no updates
[ 1.769s] telegram-api  <- bot calls sendMessage chat_id=100 text="on-message: Watchers.ZuluAudit"
[ 1.770s] telegram-api  getUpdates(offset=902) -> no updates
[ 1.897s] telegram-api  <- bot calls sendMessage chat_id=100 text="raw: update 901"
[ 1.897s] telegram-api  getUpdates(offset=902) -> no updates
[ 1.932s] user in chat 100 sends "just chatting"
[ 2.003s] telegram-api  getUpdates(offset=902) -> update 902
[ 2.003s] telegram-api  getUpdates(offset=903) -> no updates
[ 2.107s] telegram-api  <- bot calls sendMessage chat_id=100 text="on-message: Watchers.AlphaAudit"
[ 2.108s] telegram-api  getUpdates(offset=903) -> no updates
[ 2.207s] telegram-api  <- bot calls sendMessage chat_id=100 text="on-message: Watchers.MikeAudit"
[ 2.207s] telegram-api  getUpdates(offset=903) -> no updates
[ 2.331s] telegram-api  <- bot calls sendMessage chat_id=100 text="on-message: Watchers.ZuluAudit"
[ 2.332s] telegram-api  getUpdates(offset=903) -> no updates
[ 2.436s] telegram-api  <- bot calls sendMessage chat_id=100 text="raw: update 902"
[ 2.436s] telegram-api  getUpdates(offset=903) -> no updates
[ 2.530s] user in chat 100 taps the "confirm" button
[ 2.549s] telegram-api  getUpdates(offset=903) -> update 903
[ 2.550s] telegram-api  getUpdates(offset=904) -> no updates
[ 2.671s] telegram-api  <- bot calls sendMessage chat_id=100 text="routed: callback confirm"
[ 2.671s] telegram-api  getUpdates(offset=904) -> no updates
[ 2.794s] telegram-api  <- bot calls sendMessage chat_id=100 text="on-callback: Watchers.CallbackAudit"
[ 2.794s] telegram-api  getUpdates(offset=904) -> no updates
[ 2.930s] telegram-api  <- bot calls sendMessage chat_id=100 text="raw: update 903"
[ 2.931s] telegram-api  getUpdates(offset=904) -> no updates
[ 3.046s] telegram-api  <- bot calls answerCallbackQuery callback_query_id="cq-903"
[ 3.054s] telegram-api  getUpdates(offset=904) -> no updates
[ 3.111s] user 300 types "@vexel_bot search widgets"
[ 3.158s] telegram-api  getUpdates(offset=904) -> update 904
[ 3.174s] telegram-api  getUpdates(offset=905) -> no updates
[ 3.296s] telegram-api  <- bot calls answerInlineQuery inline_query_id="iq-904" cache_time=30 results=[item|widgets -> "routed: search widgets"]
[ 3.299s] telegram-api  getUpdates(offset=905) -> no updates
[ 3.441s] telegram-api  <- bot calls sendMessage chat_id=300 text="on-inline: Watchers.InlineAudit"
[ 3.442s] telegram-api  getUpdates(offset=905) -> no updates
[ 3.559s] telegram-api  <- bot calls sendMessage chat_id=300 text="raw: update 904"
[ 3.560s] telegram-api  getUpdates(offset=905) -> no updates
[ 3.653s] user 300 picks the "item|widgets" result
[ 3.666s] telegram-api  getUpdates(offset=905) -> update 905
[ 3.668s] telegram-api  getUpdates(offset=906) -> no updates
[ 3.844s] telegram-api  <- bot calls editMessageText inline_message_id="im-905" text="routed: chosen widgets"
[ 3.855s] telegram-api  getUpdates(offset=906) -> no updates
[ 3.974s] telegram-api  <- bot calls sendMessage chat_id=300 text="on-chosen: Watchers.ChosenAudit"
[ 3.975s] telegram-api  getUpdates(offset=906) -> no updates
[ 4.076s] telegram-api  <- bot calls sendMessage chat_id=300 text="raw: update 905"
[ 4.076s] telegram-api  getUpdates(offset=906) -> no updates
[ 4.105s] Information Lifetime         Application is shutting down...
[ 4.109s] Information VexelClient      VexelClient stopped
[ 4.109s] host stopped
```

## 4. Dispatch arrays the Vexel generator emitted

### `DemoBot` (`Vexel.Telegram.Routes.g.cs`)

```csharp
// <auto-generated />
#nullable enable

#pragma warning disable CS1591

namespace DemoBot;

/// <summary>Generated Vexel Telegram route registration.</summary>
[global::System.CodeDom.Compiler.GeneratedCodeAttribute("Vexel.Telegram.Generators", "2.0.0")]
public static partial class TelegramServiceCollectionExtensions
{
	/// <summary>Registers Telegram routes discovered in this assembly (DemoBot).</summary>
	public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddDemoBotTelegram(
		this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
	{
		global::System.ArgumentNullException.ThrowIfNull(services);

		var commands = new global::System.Collections.Generic.Dictionary<string, global::Vexel.Telegram.Handlers.Routing.RouteBinder>(
			global::System.StringComparer.OrdinalIgnoreCase)
		{
			["ping"] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::DemoBot.Routes.Ping.Handler>(scope);
				_ = await handler.HandleAsync(new global::DemoBot.Routes.Ping.Command(), cancellationToken).ConfigureAwait(false);
				return true;
			},
		};

		var metadata = new global::Vexel.Telegram.Handlers.Routing.CommandRouteMetadata[]
		{
			new global::Vexel.Telegram.Handlers.Routing.CommandRouteMetadata("ping", "Ping the bot"),
		};

		var callbacks = new global::System.Collections.Generic.Dictionary<string, global::Vexel.Telegram.Handlers.Routing.RouteBinder>(
			global::System.StringComparer.Ordinal)
		{
			["confirm"] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::DemoBot.Routes.Confirm.Handler>(scope);
				_ = await handler.HandleAsync(new global::DemoBot.Routes.Confirm.Command(), cancellationToken).ConfigureAwait(false);
				return true;
			},
		};

		var inlineQueries = new global::System.Collections.Generic.Dictionary<string, global::Vexel.Telegram.Handlers.Routing.RouteBinder>(
			global::System.StringComparer.Ordinal)
		{
			["search"] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::DemoBot.Routes.SearchInline.Handler>(scope);
				_ = await handler.HandleAsync(new global::DemoBot.Routes.SearchInline.Query(payload), cancellationToken).ConfigureAwait(false);
				return true;
			},
		};

		var chosenInlineResults = new global::System.Collections.Generic.Dictionary<string, global::Vexel.Telegram.Handlers.Routing.RouteBinder>(
			global::System.StringComparer.Ordinal)
		{
			["item"] = static async (scope, payload, cancellationToken) =>
			{
				var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::DemoBot.Routes.ItemChosen.Handler>(scope);
				_ = await handler.HandleAsync(new global::DemoBot.Routes.ItemChosen.Command(payload), cancellationToken).ConfigureAwait(false);
				return true;
			},
		};

		var flowSteps = new global::System.Collections.Generic.Dictionary<string, global::Vexel.Telegram.Handlers.Routing.RouteBinder>(
			global::System.StringComparer.Ordinal)
		{
		};

		var onMessages = new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry[]
		{
			new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry(
				"global::Watchers.AlphaAudit",
				static async (scope, payload, cancellationToken) =>
				{
					var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Watchers.AlphaAudit.Handler>(scope);
					_ = await handler.HandleAsync(new global::Watchers.AlphaAudit.Command(), cancellationToken).ConfigureAwait(false);
					return true;
				}),
			new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry(
				"global::Watchers.ZuluAudit",
				static async (scope, payload, cancellationToken) =>
				{
					var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Watchers.ZuluAudit.Handler>(scope);
					_ = await handler.HandleAsync(new global::Watchers.ZuluAudit.Command(), cancellationToken).ConfigureAwait(false);
					return true;
				}),
		};

		var onCallbackQueries = new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry[]
		{
			new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry(
				"global::Watchers.CallbackAudit",
				static async (scope, payload, cancellationToken) =>
				{
					var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Watchers.CallbackAudit.Handler>(scope);
					_ = await handler.HandleAsync(new global::Watchers.CallbackAudit.Command(), cancellationToken).ConfigureAwait(false);
					return true;
				}),
		};

		var onInlineQueries = new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry[]
		{
			new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry(
				"global::Watchers.InlineAudit",
				static async (scope, payload, cancellationToken) =>
				{
					var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Watchers.InlineAudit.Handler>(scope);
					_ = await handler.HandleAsync(new global::Watchers.InlineAudit.Command(), cancellationToken).ConfigureAwait(false);
					return true;
				}),
		};

		var onChosenInlineResults = new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry[]
		{
			new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry(
				"global::Watchers.ChosenAudit",
				static async (scope, payload, cancellationToken) =>
				{
					var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Watchers.ChosenAudit.Handler>(scope);
					_ = await handler.HandleAsync(new global::Watchers.ChosenAudit.Command(), cancellationToken).ConfigureAwait(false);
					return true;
				}),
		};

		_ = global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(
			services,
			new global::Vexel.Telegram.Handlers.Routing.TelegramRouteContribution(
				"DemoBot",
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

### `PluginBot` (`Vexel.Telegram.Routes.g.cs`)

```csharp
// <auto-generated />
#nullable enable

#pragma warning disable CS1591

namespace PluginBot;

/// <summary>Generated Vexel Telegram route registration.</summary>
[global::System.CodeDom.Compiler.GeneratedCodeAttribute("Vexel.Telegram.Generators", "2.0.0")]
public static partial class TelegramServiceCollectionExtensions
{
	/// <summary>Registers Telegram routes discovered in this assembly (PluginBot).</summary>
	public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddPluginBotTelegram(
		this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
	{
		global::System.ArgumentNullException.ThrowIfNull(services);

		var commands = new global::System.Collections.Generic.Dictionary<string, global::Vexel.Telegram.Handlers.Routing.RouteBinder>(
			global::System.StringComparer.OrdinalIgnoreCase)
		{
		};

		var metadata = new global::Vexel.Telegram.Handlers.Routing.CommandRouteMetadata[]
		{
		};

		var callbacks = new global::System.Collections.Generic.Dictionary<string, global::Vexel.Telegram.Handlers.Routing.RouteBinder>(
			global::System.StringComparer.Ordinal)
		{
		};

		var inlineQueries = new global::System.Collections.Generic.Dictionary<string, global::Vexel.Telegram.Handlers.Routing.RouteBinder>(
			global::System.StringComparer.Ordinal)
		{
		};

		var chosenInlineResults = new global::System.Collections.Generic.Dictionary<string, global::Vexel.Telegram.Handlers.Routing.RouteBinder>(
			global::System.StringComparer.Ordinal)
		{
		};

		var flowSteps = new global::System.Collections.Generic.Dictionary<string, global::Vexel.Telegram.Handlers.Routing.RouteBinder>(
			global::System.StringComparer.Ordinal)
		{
		};

		var onMessages = new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry[]
		{
			new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry(
				"global::Watchers.MikeAudit",
				static async (scope, payload, cancellationToken) =>
				{
					var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Watchers.MikeAudit.Handler>(scope);
					_ = await handler.HandleAsync(new global::Watchers.MikeAudit.Command(), cancellationToken).ConfigureAwait(false);
					return true;
				}),
		};

		var onCallbackQueries = new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry[]
		{
		};

		var onInlineQueries = new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry[]
		{
		};

		var onChosenInlineResults = new global::Vexel.Telegram.Handlers.Routing.OnHandlerEntry[]
		{
		};

		_ = global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(
			services,
			new global::Vexel.Telegram.Handlers.Routing.TelegramRouteContribution(
				"PluginBot",
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

