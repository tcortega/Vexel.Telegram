using System.ComponentModel;
using Axon.Telegram.Commands.Services;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Results;
using Telegram.Bot;

namespace Axon.Telegram.Sample.Commands;

/// <summary>
/// A command group containing general-purpose commands.
/// </summary>
public class GeneralCommands(ITelegramBotClient botClient, ICommandContext context) : CommandGroup
{
    [Command("ping"), Description("Checks if the bot is responsive.")]
    public async Task<IResult> PingAsync()
    {
        // We can safely access the context because the framework guarantees it's available here.
        var telegramContext = (TelegramCommandContext)context;

        await botClient.SendMessage
        (
            chatId: telegramContext.Message.Chat.Id,
            text: "Pong!",
            replyParameters: telegramContext.Message.MessageId,
            cancellationToken: CancellationToken
        );

        return Result.FromSuccess();
    }

    [Command("echo"), Description("Repeats the text you provide.")]
    public async Task<IResult> EchoAsync([Greedy] string text)
    {
        var telegramContext = (TelegramCommandContext)context;

        await botClient.SendMessage
        (
            chatId: telegramContext.Message.Chat.Id,
            text: $"You said: {text}",
            replyParameters: telegramContext.Message.MessageId,
            cancellationToken: CancellationToken
        );

        return Result.FromSuccess();
    }
}