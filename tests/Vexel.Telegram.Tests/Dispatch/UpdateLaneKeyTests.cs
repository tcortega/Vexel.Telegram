using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Vexel.Telegram.Client.Dispatch;

namespace Vexel.Telegram.Tests.Dispatch;

public sealed class UpdateLaneKeyTests
{
	[Fact]
	public void Message_UsesChatId()
	{
		var update = new Update
		{
			Id = 1,
			Message = new Message
			{
				Chat = new Chat { Id = 100 },
			},
		};

		Assert.Equal(100, UpdateLaneKey.FromUpdate(update));
	}

	[Fact]
	public void CallbackQuery_UsesMessageChatId_WhenPresent()
	{
		var update = new Update
		{
			Id = 2,
			CallbackQuery = new CallbackQuery
			{
				Id = "cb",
				From = new User { Id = 9, IsBot = false, FirstName = "u" },
				Message = new Message { Chat = new Chat { Id = 200 } },
				ChatInstance = "x",
			},
		};

		Assert.Equal(200, UpdateLaneKey.FromUpdate(update));
	}

	[Fact]
	public void CallbackQuery_FallsBackToFromId_WhenChatless()
	{
		var update = new Update
		{
			Id = 3,
			CallbackQuery = new CallbackQuery
			{
				Id = "cb",
				From = new User { Id = 9, IsBot = false, FirstName = "u" },
				InlineMessageId = "inline-msg",
				ChatInstance = "x",
			},
		};

		Assert.Equal(9, UpdateLaneKey.FromUpdate(update));
	}

	[Fact]
	public void InlineQuery_UsesFromId()
	{
		var update = new Update
		{
			Id = 4,
			InlineQuery = new InlineQuery
			{
				Id = "iq",
				From = new User { Id = 11, IsBot = false, FirstName = "u" },
				Query = "q",
				Offset = "",
			},
		};

		Assert.Equal(11, UpdateLaneKey.FromUpdate(update));
	}

	[Fact]
	public void ChosenInlineResult_UsesFromId()
	{
		var update = new Update
		{
			Id = 5,
			ChosenInlineResult = new ChosenInlineResult
			{
				ResultId = "r",
				From = new User { Id = 12, IsBot = false, FirstName = "u" },
				Query = "q",
			},
		};

		Assert.Equal(12, UpdateLaneKey.FromUpdate(update));
	}

	[Fact]
	public void Poll_UsesGlobalLane()
	{
		var update = new Update
		{
			Id = 6,
			Poll = new Poll
			{
				Id = "p",
				Question = "q",
				Options = [],
				TotalVoterCount = 0,
				IsClosed = false,
				IsAnonymous = true,
				Type = PollType.Regular,
				AllowsMultipleAnswers = false,
			},
		};

		Assert.Equal(UpdateLaneKey.Global, UpdateLaneKey.FromUpdate(update));
	}
}
