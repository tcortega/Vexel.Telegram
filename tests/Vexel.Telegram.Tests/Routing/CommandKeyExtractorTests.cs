using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Vexel.Telegram.Handlers.Routing;

namespace Vexel.Telegram.Tests.Routing;

public sealed class CommandKeyExtractorTests
{
	[Fact]
	public void Extracts_plain_command_and_args()
	{
		var message = CommandMessage("/ping hello world");
		Assert.True(CommandKeyExtractor.TryExtract(message, botUsername: "MyBot", out var command, out var args));
		Assert.Equal("ping", command);
		Assert.Equal("hello world", args);
	}

	[Fact]
	public void Accepts_matching_bot_suffix()
	{
		var message = CommandMessage("/ping@MyBot rest");
		Assert.True(CommandKeyExtractor.TryExtract(message, botUsername: "mybot", out var command, out var args));
		Assert.Equal("ping", command);
		Assert.Equal("rest", args);
	}

	[Fact]
	public void Rejects_foreign_bot_suffix()
	{
		var message = CommandMessage("/ping@OtherBot");
		Assert.False(CommandKeyExtractor.TryExtract(message, botUsername: "MyBot", out _, out _));
	}

	[Fact]
	public void Ignores_caption_only_messages()
	{
		var message = new Message
		{
			Id = 1,
			Date = DateTime.UtcNow,
			Chat = new Chat { Id = 1 },
			Caption = "/ping",
			CaptionEntities =
			[
				new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 0, Length = 5 },
			],
		};

		Assert.False(CommandKeyExtractor.TryExtract(message, botUsername: null, out _, out _));
	}

	private static Message CommandMessage(string text)
	{
		var slash = text.IndexOf(' ', StringComparison.Ordinal);
		var entityLength = slash < 0 ? text.Length : slash;
		return new Message
		{
			Id = 1,
			Date = DateTime.UtcNow,
			Chat = new Chat { Id = 1 },
			Text = text,
			Entities =
			[
				new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 0, Length = entityLength },
			],
		};
	}
}
