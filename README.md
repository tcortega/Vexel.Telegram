# Vexel.Telegram

[![NuGet](https://img.shields.io/nuget/v/Vexel.Telegram.svg?style=plastic)](https://www.nuget.org/packages/Vexel.Telegram/)
[![GitHub release](https://img.shields.io/github/release/tcortega/Vexel.Telegram.svg)](https://GitHub.com/tcortega/Vexel.Telegram/releases/)
[![GitHub license](https://img.shields.io/github/license/tcortega/Vexel.Telegram.svg)](https://github.com/tcortega/Vexel.Telegram/blob/master/LICENSE)
[![GitHub issues](https://img.shields.io/github/issues/tcortega/Vexel.Telegram.svg)](https://GitHub.com/tcortega/Vexel.Telegram/issues/)
[![GitHub issues-closed](https://img.shields.io/github/issues-closed/tcortega/Vexel.Telegram.svg)](https://GitHub.com/tcortega/Vexel.Telegram/issues?q=is%3Aissue+is%3Aclosed)
[![GitHub Actions](https://github.com/tcortega/Vexel.Telegram/actions/workflows/build.yml/badge.svg)](https://github.com/tcortega/Vexel.Telegram/actions)
---

Vexel.Telegram is a C# library for building Telegram bots using
the [Telegram.Bot](https://github.com/TelegramBots/Telegram.Bot) library.
It is built to fulfill a need for robust, feature-complete, highly available and concurrent bots.

It is heavily based on [Remora.Discord](https://github.com/Remora/Remora.Discord), a Discord bot framework, and shares
many of its design principles and philosophies.

#### Examples

* [Vexel.Telegram.Sample](./samples/Vexel.Telegram.Sample)

## Installing Vexel.Telegram

You can install [Vexel.Telegram with NuGet](https://www.nuget.org/packages/Vexel.Telegram):

	Install-Package Vexel.Telegram

Or via the .NET Core command line interface:

	dotnet add package Vexel.Telegram

Either commands, from Package Manager Console or .NET Core CLI, will download and install Vexel.Telegram.

Do note that the `Vexel.Telegram` package is a metapackage, which means it will install the latest versions of all the
Vexel.Telegram packages, regardless of API compatibility. Production packages/projects should probably prefer explicit
dependencies
instead.

## Using Vexel.Telegram

### Adding Vexel.Telegram to your project

For applications using the .NET Generic Host, the `Vexel.Telegram.Hosting` package is the recommended way to register
your
bot. It simplifies configuration and manages the bot's lifecycle as a background service.

To add it to your Hosting project, all you need to do is call the `.AddTelegramService(_ => "<BOT_TOKEN>")` method
either on the `IServiceCollection` or the `IHostBuilder`:

To add the bot, call `AddTelegramService()` on either your `IHostBuilder` or your `IServiceCollection`. The best
practice is to load your bot token from .NET's configuration system (e.g., appsettings.json or environment variables).

```csharp
using Vexel.Telegram.Hosting.Extensions;
using Microsoft.Extensions.DependencyInjection;

var host = Host.CreateDefaultBuilder(args)
	.AddTelegramService(_ => "<BOT_TOKEN>")
	.Build();

await host.RunAsync();
```

### Creating a Command

Create a Command by adding the following code to your project:

```csharp
using Vexel.Telegram.Commands;

public sealed class ExampleCommands(IFeedbackService feedbackService, ITextCommandContext context) : CommandGroup
{
	[Command("ping"), Description("Replies with 'Pong!'")]
	public async Task<IResult> PingAsync() 
	{
		var options = new FeedbackMessageOptions 
		{ 
			ReplyParameters = new ReplyParameters { MessageId = context.Message.MessageId } 
		};
		
		var result = await feedbackService.SendContextualMessageAsync("Pong!", options: options, ct: CancellationToken);
		return result.IsSuccess ? Result.FromSuccess() : Result.FromError(result.Error);
	}
	
	[Command("success"), Description("Sends a success message")]
	public async Task<IResult> SuccessAsync() 
	{
		var result = await feedbackService.SendContextualSuccessAsync("Operation completed successfully!", ct: CancellationToken);
		return result.IsSuccess ? Result.FromSuccess() : Result.FromError(result.Error);
	}
	
	[Command("pay"), Description("Starts a payment process with the given amount")]
	public Task<IResult> PayAsync(decimal amount) 
	{
		return Result.FromSuccess();
	}
}
```

You can access everything you may need, such as the `IFeedbackService` and the `ITextCommandContext`, through
dependency injection. Parameters are automatically bound and parsed from the command arguments.

For more information regarding the command system, please refer
to [Remora.Commands](https://github.com/Remora/Remora.Commands).

### Registering the commands

To enable command handling, you must register the command framework and your command classes with the service
collection.

Start by calling the `AddTelegramCommands()` extension method inside your service configuration (e.g., in `Program.cs`).
This method sets up the necessary infrastructure. From there, you can chain methods from the underlying
`Remora.Commands`
library to build your command tree and add your command groups.

```csharp
services.AddTelegramCommands()
	.AddCommandTree()
	.WithCommandGroup<ExampleCommands>();
```

### Creating Interactions

In Vexel, incoming queries such as callback queries, inline queries, and others are collectively known as
"interactions." The framework routes these to dedicated handlers, similar to how commands work. To create a handler,
simply inherit from the `InteractionGroup` class.

The framework provides attributes to handle different interaction types automatically:

1. **CallbackButton**: For handling callback queries from inline buttons.
2. **InlineQuery**: For handling inline queries.
3. **ChosenInlineResult**: For handling chosen inline results.
4. **TextResponse**: For handling text messages.

Here is an example of a simple interaction handler:

```csharp
using Vexel.Telegram.Commands;

public sealed class SampleInteractions(ITelegramBotClient botClient, IFeedbackService feedbackService, IInteractionContext context) : InteractionGroup
{
    [CallbackButton("ping"), Description("Responds to a ping button press")]
    public async Task<IResult> HandlePingButtonAsync()
    {
        var callbackQuery = context.Interaction.AsT0;

        await botClient.AnswerCallbackQuery(
            callbackQueryId: callbackQuery.Id,
            text: "🏓 Pong! Interaction received!",
            showAlert: true,
            cancellationToken: CancellationToken
        );

        if (callbackQuery.Message is not null)
        {
            var result = await feedbackService.EditContextualMessageAsync(
                callbackQuery.Message.MessageId,
                "🏓 **Pong!** Button interaction received!",
                ParseMode.Markdown,
                ct: CancellationToken
            );
            
            if (!result.IsSuccess)
                return Result.FromError(result.Error);
        }

        return Result.FromSuccess();
    }
}
```

While you can construct interaction components manually using `InteractionIdHelper`, it is recommended to use the
provided builders (e.g., `InlineKeyboardBuilder`) for creating inline keyboards and other components.

#### Working with Messages as Interactions

A significant portion of user input on Telegram occurs through text messages directly within a chat.
Vexel addresses this by handling text messages as a form of interaction, enabling the creation of
conversational workflows.

This functionality is achieved by combining the `TextResponse` attribute with the `IConversationStateService`.
You can set a user's state to "awaiting input" and specify which handler method should process their next message.
When the user replies, the framework routes their message to the handler decorated with the corresponding
`TextResponse` attribute.

The following example demonstrates a simple payment workflow where the bot requests an amount after a button is pressed:

```csharp
using Vexel.Telegram.Commands;

public sealed class PaymentInteractions(ITelegramBotClient botClient, IFeedbackService feedbackService, 
	IConversationStateService conversationState, IInteractionCommandContext context) : InteractionGroup
{
	[CallbackButton(nameof(StartPaymentAsync))]
	public async Task<IResult> StartPaymentAsync()
	{
		var callbackQuery = context.Interaction.AsT0;
		var chatId = callbackQuery.Message.Chat.Id;
		
		// Set the conversation state to await input for the 'ReceivePaymentAmountAsync' handler.
		conversationState.SetAwaitingInput
		(
			chatId,
			context.User.Id,
			nameof(ReceivePaymentAmountAsync)
		);
	
		await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: CancellationToken);
		return Result.FromSuccess();
	}
	
	[TextResponse(nameof(ReceivePaymentAmountAsync))]
	public async Task<IResult> ReceivePaymentAmountAsync(decimal input)
	{
		var result = await feedbackService.SendContextualSuccessAsync(
			$"Thank you! Payment process for ${input:F2} started.",
			ct: CancellationToken
		);
	
		return result.IsSuccess ? Result.FromSuccess() : Result.FromError(result.Error);
	}
}
```

### Registering Interaction Handlers

To enable interaction handling, you must register the interactivity framework and your interaction groups with the
service collection.

```csharp
services.AddTelegramInteractivity();
services.AddInteractionGroup<SampleInteractions>();
```

### Creating Responders

Vexel uses a dispatcher/responder model to process incoming updates. This allows you to create focused responders that
handle specific update types, such as new messages, message edits, or callback queries. To create a responder, simply
implement the `IResponder<TUpdate>` interface.

The `TUpdate` generic parameter can be any Telegram update type from the `Telegram.Bot.Types` namespace, like `Message`
or
`CallbackQuery`.

```csharp
using Vexel.Telegram.Commands;

public class MessageResponder(ILogger<MessageResponder> logger, IFeedbackService feedbackService)
	: IResponder<Message>
{
	public async Task<Result> RespondAsync(Message message, CancellationToken ct = default)
	{
		if (string.IsNullOrEmpty(message.Text))
		{
			return Result.FromSuccess();
		}

		logger.LogInformation("Received message from {User}: {Text}", message.From?.Username ?? "Unknown User",
			message.Text);

		// Simple echo logic
		var result = await feedbackService.SendMessageAsync(
			message.Chat.Id,
			$"Here's a hello from the MessageResponder: {message.Text}",
			ct: ct
		);

		if (!result.IsSuccess)
		{
			logger.LogError("Failed to send message: {Error}", result.Error);
			return Result.FromError(result.Error);
		}

		return Result.FromSuccess();
	}
}
```

### Registering Responders

For your responders to be active, they must be registered with the service collection. You can do this by calling the
`AddResponder` extension method for each responder class.

```csharp
services.AddResponder<MessageResponder>();
```

This ensures that the dispatcher will forward the appropriate updates to your responder for processing.

## Feedback Service

The `IFeedbackService` provides a convenient and unified way to send messages to users without dealing directly with the Telegram Bot API. It offers contextual messaging, semantic methods for different message types (success, error, warning, info), and comprehensive support for media messages.

### Quick Examples

```csharp
// Send a success message with emoji prefix
await feedbackService.SendContextualSuccessAsync("Operation completed!");

// Send a message with reply
var options = new FeedbackMessageOptions 
{ 
    ReplyParameters = new ReplyParameters { MessageId = originalMessageId } 
};
await feedbackService.SendContextualMessageAsync("Hello!", options: options);

// Send media
await feedbackService.SendContextualPhotoAsync(
    photo: InputFile.FromUri("https://example.com/image.jpg"),
    caption: "Check this out!"
);
```

The service is automatically registered when you call `AddTelegramCommands()` and returns `Result<T>` types for consistent error handling.

For complete documentation of all available methods and options, see the [IFeedbackService interface](./src/Vexel.Telegram.Commands/Feedback/IFeedbackService.cs).
