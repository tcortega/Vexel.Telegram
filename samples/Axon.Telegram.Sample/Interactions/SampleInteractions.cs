using System.ComponentModel;
using Axon.Telegram.Interactivity;
using Axon.Telegram.Interactivity.Attributes;
using Axon.Telegram.Interactivity.Builders;
using Axon.Telegram.Interactivity.Contexts;
using Remora.Commands.Attributes;
using Remora.Results;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;

namespace Axon.Telegram.Sample.Interactions;

/// <summary>
/// Sample interaction handlers demonstrating callback buttons, inline queries, and chosen results.
/// </summary>
public class SampleInteractions(ITelegramBotClient botClient, IInteractionCommandContext context) : InteractionGroup
{
    [CallbackButton("ping"), Description("Responds to ping button presses")]
    public async Task<IResult> HandlePingButtonAsync()
    {
        var callbackQuery = context.Interaction.AsT0;

        await botClient.AnswerCallbackQuery(
            callbackQueryId: callbackQuery.Id,
            text: "🏓 Pong! Button interaction received!",
            showAlert: true,
            cancellationToken: CancellationToken
        );

        if (callbackQuery.Message is not null)
        {
            await botClient.EditMessageText(
                chatId: callbackQuery.Message.Chat.Id,
                messageId: callbackQuery.Message.MessageId,
                text:
                $"🏓 **Pong!**\n\nButton pressed by: {context.User.FirstName}\nTime: {DateTime.UtcNow:HH:mm:ss} UTC",
                parseMode: ParseMode.Markdown,
                cancellationToken: CancellationToken
            );
        }

        return Result.FromSuccess();
    }

    [CallbackButton("color"), Description("Shows a color selection menu")]
    public async Task<IResult> HandleColorButtonAsync()
    {
        var callbackQuery = context.Interaction.AsT0;

        var keyboard = new InlineKeyboardBuilder()
            .AddRow()
            .AddCallbackButton("🔴 Red", "select_color", "red")
            .AddCallbackButton("🟢 Green", "select_color", "green")
            .AddCallbackButton("🔵 Blue", "select_color", "blue")
            .AddRow()
            .AddCallbackButton("🟡 Yellow", "select_color", "yellow")
            .AddCallbackButton("🟣 Purple", "select_color", "purple")
            .AddCallbackButton("🟠 Orange", "select_color", "orange")
            .AddRow()
            .AddCallbackButton("⬅️ Back", "back_to_menu")
            .Build();

        if (callbackQuery.Message is not null)
        {
            await botClient.EditMessageText(
                chatId: callbackQuery.Message.Chat.Id,
                messageId: callbackQuery.Message.MessageId,
                text: "🎨 **Color Selection**\n\nChoose your favorite color:",
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard,
                cancellationToken: CancellationToken
            );
        }

        await botClient.AnswerCallbackQuery(
            callbackQueryId: callbackQuery.Id,
            cancellationToken: CancellationToken
        );

        return Result.FromSuccess();
    }

    [CallbackButton("select_color"), Description("Handles color selection")]
    public async Task<IResult> HandleColorSelectionAsync([Option("state")] string? color)
    {
        var callbackQuery = context.Interaction.AsT0;

        if (string.IsNullOrEmpty(color))
        {
            await botClient.AnswerCallbackQuery(
                callbackQueryId: callbackQuery.Id,
                text: "❌ No color data found!",
                showAlert: true,
                cancellationToken: CancellationToken
            );
            return Result.FromSuccess();
        }

        var colorEmoji = color switch
        {
            "red" => "🔴",
            "green" => "🟢",
            "blue" => "🔵",
            "yellow" => "🟡",
            "purple" => "🟣",
            "orange" => "🟠",
            _ => "⚪"
        };

        await botClient.AnswerCallbackQuery(
            callbackQueryId: callbackQuery.Id,
            text: $"You selected {colorEmoji} {color}!",
            cancellationToken: CancellationToken
        );

        if (callbackQuery.Message is null) return Result.FromSuccess();

        var keyboard = new InlineKeyboardBuilder()
            .AddRow()
            .AddCallbackButton("🎨 Choose Another Color", "color")
            .AddCallbackButton("🏠 Back to Menu", "back_to_menu")
            .Build();

        await botClient.EditMessageText(
            chatId: callbackQuery.Message.Chat.Id,
            messageId: callbackQuery.Message.MessageId,
            text: $"✅ **Color Selected!**\n\nYou chose: {colorEmoji} {color}\n\nWhat would you like to do next?",
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard,
            cancellationToken: CancellationToken
        );

        return Result.FromSuccess();
    }

    [CallbackButton("back_to_menu"), Description("Returns to the main demo menu")]
    public async Task<IResult> HandleBackToMenuAsync()
    {
        var callbackQuery = context.Interaction.AsT0;

        var keyboard = new InlineKeyboardBuilder()
            .AddRow()
            .AddCallbackButton("🏓 Ping", "ping")
            .AddRow()
            .AddCallbackButton("🎨 Colors", "color")
            .AddUrlButton("🌐 Telegram", "https://telegram.org")
            .Build();

        if (callbackQuery.Message is not null)
        {
            await botClient.EditMessageText(
                chatId: callbackQuery.Message.Chat.Id,
                messageId: callbackQuery.Message.MessageId,
                text: "🎮 **Interactive Demo Menu**\n\nChoose an option to test different interactions:",
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard,
                cancellationToken: CancellationToken
            );
        }

        await botClient.AnswerCallbackQuery(
            callbackQueryId: callbackQuery.Id,
            cancellationToken: CancellationToken
        );

        return Result.FromSuccess();
    }

    [CallbackButton("counter"), Description("Demonstrates a counter with state")]
    public async Task<IResult> HandleCounterButtonAsync([Option("state")] string? countStr)
    {
        var callbackQuery = context.Interaction.AsT0;

        if (!int.TryParse(countStr, out var count))
        {
            count = 0;
        }

        count++;

        if (callbackQuery.Message is not null)
        {
            var keyboard = new InlineKeyboardBuilder()
                .AddRow()
                .AddCallbackButton($"🔢 Count: {count} (Click to increment)", "counter", count.ToString())
                .AddRow()
                .AddCallbackButton("🔄 Reset", "counter", "0")
                .AddCallbackButton("🏠 Back", "back_to_menu")
                .Build();

            await botClient.EditMessageText(
                chatId: callbackQuery.Message.Chat.Id,
                messageId: callbackQuery.Message.MessageId,
                text:
                $"🔢 **Counter Demo**\n\nCurrent count: **{count}**\n\nThe count is stored in the button's state data!",
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard,
                cancellationToken: CancellationToken
            );
        }

        await botClient.AnswerCallbackQuery(
            callbackQueryId: callbackQuery.Id,
            text: $"Count is now {count}!",
            cancellationToken: CancellationToken
        );

        return Result.FromSuccess();
    }

    [InlineQuery("search"), Description("Handles inline search queries")]
    public async Task<IResult> HandleSearchInlineQueryAsync([Greedy] string query)
    {
        var inlineQuery = context.Interaction.AsT1;

        var results = new List<InlineQueryResult>();

        if (string.IsNullOrWhiteSpace(query))
        {
            results.Add(new InlineQueryResultArticle(
                id: "help",
                title: "How to use inline search",
                inputMessageContent: new InputTextMessageContent("To search, type: @bot search <your query>")
            ));
        }
        else
        {
            for (int i = 1; i <= 5; i++)
            {
                results.Add(new InlineQueryResultArticle(
                    id: $"result_{i}_{query}",
                    title: $"Result {i} for '{query}'",
                    inputMessageContent: new InputTextMessageContent(
                        $"You searched for: **{query}**\n\nThis is result #{i}")
                ));
            }
        }

        await botClient.AnswerInlineQuery(
            inlineQueryId: inlineQuery.Id,
            results: results,
            cacheTime: 300,
            isPersonal: true,
            cancellationToken: CancellationToken
        );

        return Result.FromSuccess();
    }

    [InlineQuery(""), Description("Handles empty inline queries")]
    public async Task<IResult> HandleEmptyInlineQueryAsync()
    {
        var inlineQuery = context.Interaction.AsT1;

        var results = new List<InlineQueryResult>
        {
            new InlineQueryResultArticle(
                id: "welcome",
                title: "Welcome to Inline Mode!",
                inputMessageContent: new InputTextMessageContent(
                    "👋 **Welcome to inline mode!**\n\nTry typing 'search' followed by your query.")
            ),
            new InlineQueryResultArticle(
                id: "time",
                title: "Current Time",
                inputMessageContent:
                new InputTextMessageContent($"🕐 Current UTC time: **{DateTime.UtcNow:HH:mm:ss}**")
            ),
            new InlineQueryResultArticle(
                id: "random",
                title: "Random Number",
                inputMessageContent: new InputTextMessageContent($"🎲 Random number: **{Random.Shared.Next(1, 100)}**")
            )
        };

        await botClient.AnswerInlineQuery(
            inlineQueryId: inlineQuery.Id,
            results: results,
            cacheTime: 0,
            isPersonal: true,
            cancellationToken: CancellationToken
        );

        return Result.FromSuccess();
    }

    [ChosenInlineResult("result"), Description("Tracks when users select a search result")]
    public async Task<IResult> HandleChosenSearchResultAsync([Option("state")] string? resultData)
    {
        var chosenResult = context.Interaction.AsT2;

        Console.WriteLine(
            $"User {context.User.FirstName} (ID: {context.User.Id}) selected result: {chosenResult.ResultId}");

        if (!string.IsNullOrEmpty(resultData))
        {
            Console.WriteLine($"Result data: {resultData}");
        }

        return Result.FromSuccess();
    }
}