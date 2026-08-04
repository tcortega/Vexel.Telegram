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

	[Fact]
	public void Command_without_arguments_yields_empty_args()
	{
		var message = CommandMessage("/start");
		Assert.True(CommandKeyExtractor.TryExtract(message, botUsername: null, out var command, out var args));
		Assert.Equal("start", command);
		Assert.Equal(string.Empty, args);
	}

	[Fact]
	public void Trims_argument_whitespace()
	{
		var message = CommandMessage("/ping   hello  ");
		Assert.True(CommandKeyExtractor.TryExtract(message, botUsername: null, out _, out var args));
		Assert.Equal("hello", args);
	}

	[Fact]
	public void Mid_message_bot_command_entity_is_ignored()
	{
		var message = new Message
		{
			Id = 1,
			Date = DateTime.UtcNow,
			Chat = new Chat { Id = 1 },
			Text = "see /ping later",
			Entities =
			[
				new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 4, Length = 5 },
			],
		};

		Assert.False(CommandKeyExtractor.TryExtract(message, botUsername: null, out _, out _));
	}

	[Fact]
	public void Unknown_bot_username_accepts_suffixed_command()
	{
		// Router resolves username lazily; extractor with null username keeps the command.
		var message = CommandMessage("/ping@AnyBot args");
		Assert.True(CommandKeyExtractor.TryExtract(message, botUsername: null, out var command, out var args));
		Assert.Equal("ping", command);
		Assert.Equal("args", args);
	}

	[Fact]
	public void Split_overload_reports_bot_suffix_separately()
	{
		var message = CommandMessage("/stats@MyBot");
		Assert.True(CommandKeyExtractor.TryExtract(message, out var command, out var suffix, out var args));
		Assert.Equal("stats", command);
		Assert.Equal("MyBot", suffix);
		Assert.Equal(string.Empty, args);
	}

	[Fact]
	public void Split_overload_reports_null_suffix_when_absent()
	{
		var message = CommandMessage("/stats now");
		Assert.True(CommandKeyExtractor.TryExtract(message, out var command, out var suffix, out var args));
		Assert.Equal("stats", command);
		Assert.Null(suffix);
		Assert.Equal("now", args);
	}

	[Fact]
	public void Bare_at_command_is_rejected()
	{
		// Entity body is "/@Bot" → empty command name after stripping suffix.
		var message = CommandMessage("/@Bot");
		Assert.False(CommandKeyExtractor.TryExtract(message, out _, out _, out _));
	}

	[Fact]
	public void Empty_text_is_not_a_command()
	{
		var message = new Message
		{
			Id = 1,
			Date = DateTime.UtcNow,
			Chat = new Chat { Id = 1 },
			Text = string.Empty,
			Entities =
			[
				new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 0, Length = 0 },
			],
		};

		Assert.False(CommandKeyExtractor.TryExtract(message, botUsername: null, out _, out _));
	}

	[Fact]
	public void Non_command_entity_at_offset_zero_is_ignored()
	{
		var message = new Message
		{
			Id = 1,
			Date = DateTime.UtcNow,
			Chat = new Chat { Id = 1 },
			Text = "/notreally",
			Entities =
			[
				new MessageEntity { Type = MessageEntityType.Mention, Offset = 0, Length = 10 },
			],
		};

		Assert.False(CommandKeyExtractor.TryExtract(message, botUsername: null, out _, out _));
	}

	[Theory]
	[InlineData("/Ping", "Ping")]
	[InlineData("/PING", "PING")]
	[InlineData("/pInG", "pInG")]
	public void Preserves_command_casing_from_message(string text, string expectedCommand)
	{
		// Case folding is the router's job (ordinal-ignore-case map); extractor keeps the raw body.
		var message = CommandMessage(text);
		Assert.True(CommandKeyExtractor.TryExtract(message, botUsername: null, out var command, out _));
		Assert.Equal(expectedCommand, command);
	}

	[Fact]
	public void Extract_is_idempotent_for_common_shapes()
	{
		string[] samples =
		[
			"/start",
			"/ping hello",
			"/Ping@MyBot x",
			"/add 1 2",
			"/cancel",
		];

		foreach (var sample in samples)
		{
			var message = CommandMessage(sample);
			Assert.True(CommandKeyExtractor.TryExtract(message, out var c1, out var s1, out var a1));
			Assert.True(CommandKeyExtractor.TryExtract(message, out var c2, out var s2, out var a2));
			Assert.Equal(c1, c2);
			Assert.Equal(s1, s2);
			Assert.Equal(a1, a2);
		}
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
