using System.ComponentModel;
using Axon.Telegram.Commands.Contexts;
using Axon.Telegram.Interactivity.Builders;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Results;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Axon.Telegram.Sample.Commands;

/// <summary>
/// A command group containing general-purpose commands.
/// </summary>
public class GeneralCommands(ITelegramBotClient botClient, ITextCommandContext context) : CommandGroup
{
    [Command("ping"), Description("Checks if the bot is responsive.")]
    public async Task<IResult> PingAsync()
    {
        await botClient.SendMessage
        (
            chatId: context.Message.Chat.Id,
            text: "Pong!",
            replyParameters: context.Message.MessageId,
            cancellationToken: CancellationToken
        );

        return Result.FromSuccess();
    }

    [Command("echo"), Description("Repeats the text you provide.")]
    public async Task<IResult> EchoAsync([Greedy] string text)
    {
        await botClient.SendMessage
        (
            chatId: context.Message.Chat.Id,
            text: $"You said: {text}",
            replyParameters: context.Message.MessageId,
            cancellationToken: CancellationToken
        );

        return Result.FromSuccess();
    }

    [Command("demo"), Description("Shows the interactive demo with buttons and callbacks.")]
    public async Task<IResult> DemoAsync()
    {
        var keyboard = new InlineKeyboardBuilder()
            .AddRow()
            .AddCallbackButton("🏓 Ping", "ping")
            .AddRow()
            .AddCallbackButton("🎨 Colors", "color")
            .AddUrlButton("🌐 Telegram", "https://telegram.org")
            .Build();

        await botClient.SendMessage
        (
            chatId: context.Message.Chat.Id,
            text: "🎮 **Interactive Demo Menu**\n\nChoose an option to test different interactions:",
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard,
            cancellationToken: CancellationToken
        );

        return Result.FromSuccess();
    }

    [Command("inline"), Description("Shows how to use inline queries.")]
    public async Task<IResult> InlineAsync()
    {
        var keyboard = new InlineKeyboardBuilder()
            .AddRow()
            .AddSwitchInlineQueryCurrentChatButton("🔍 Try Inline Query Here", "hello")
            .AddRow()
            .AddSwitchInlineQueryButton("🔍 Try Inline Query Anywhere", "telegram")
            .Build();

        await botClient.SendMessage
        (
            chatId: context.Message.Chat.Id,
            text: "🔍 **Inline Query Demo**\n\nClick the buttons below to try inline queries!\n\n" +
                  "You can also try typing `@yourbotname query` in any chat to use inline mode.",
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard,
            cancellationToken: CancellationToken
        );

        return Result.FromSuccess();
    }
}