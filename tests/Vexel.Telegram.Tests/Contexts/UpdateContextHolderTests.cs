using Telegram.Bot.Types;
using Vexel.Telegram.Handlers.Contexts;

namespace Vexel.Telegram.Tests.Contexts;

public sealed class UpdateContextHolderTests
{
	[Fact]
	public void Set_CalledTwice_Throws()
	{
		var holder = new UpdateContextHolder();
		holder.Set(MessageUpdate(1, chatId: 10));

		var ex = Assert.Throws<InvalidOperationException>(() => holder.Set(MessageUpdate(2, chatId: 11)));

		Assert.Contains("once", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Get_WhenUnset_ThrowsClearOutsideScopeMessage()
	{
		var holder = new UpdateContextHolder();

		var ex = Assert.Throws<InvalidOperationException>(() => _ = holder.Message);

		Assert.Contains("resolved outside a Vexel update scope", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Set_MessageUpdate_ExposesMessageContextOnly()
	{
		var holder = new UpdateContextHolder();
		holder.Set(MessageUpdate(1, chatId: 42));

		Assert.NotNull(holder.Message);
		Assert.Equal(42, holder.Message.ChatId);
		Assert.Null(holder.Callback);
		Assert.Null(holder.InlineQuery);
		Assert.Null(holder.ChosenInlineResult);
	}

	[Fact]
	public void Set_CallbackWithoutMessage_ChatIdIsNull()
	{
		var holder = new UpdateContextHolder();
		holder.Set(new Update
		{
			Id = 1,
			CallbackQuery = new CallbackQuery
			{
				Id = "cb-1",
				From = new User { Id = 7, IsBot = false, FirstName = "a" },
				ChatInstance = "x",
				Data = "go",
			},
		});

		Assert.NotNull(holder.Callback);
		Assert.Null(holder.Callback.ChatId);
		Assert.Null(holder.Callback.Message);
		Assert.Equal(7, holder.Callback.From.Id);
	}

	[Fact]
	public void Set_CallbackWithMessage_ChatIdMatchesMessageChat()
	{
		var holder = new UpdateContextHolder();
		holder.Set(new Update
		{
			Id = 1,
			CallbackQuery = new CallbackQuery
			{
				Id = "cb-2",
				From = new User { Id = 7, IsBot = false, FirstName = "a" },
				ChatInstance = "x",
				Data = "go",
				Message = new Message
				{
					Id = 9,
					Date = DateTime.UtcNow,
					Chat = new Chat { Id = 99 },
				},
			},
		});

		Assert.Equal(99, holder.Callback!.ChatId);
		Assert.Equal(9, holder.Callback.MessageId);
	}

	private static Update MessageUpdate(int id, long chatId) =>
		new()
		{
			Id = id,
			Message = new Message
			{
				Id = id,
				Chat = new Chat { Id = chatId },
				Date = DateTime.UtcNow,
				Text = $"m-{id}",
			},
		};
}
