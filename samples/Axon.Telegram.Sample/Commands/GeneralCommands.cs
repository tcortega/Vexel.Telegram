using System.ComponentModel;
using Axon.Telegram.Commands.Contexts;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Results;
using Telegram.Bot;

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
}