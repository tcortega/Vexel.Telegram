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

	[Fact]
	public void EditedMessage_UsesChatId()
	{
		var update = new Update
		{
			Id = 7,
			EditedMessage = new Message
			{
				Chat = new Chat { Id = 300 },
			},
		};

		Assert.Equal(300, UpdateLaneKey.FromUpdate(update));
	}

	[Fact]
	public void ChannelPost_UsesChatId()
	{
		var update = new Update
		{
			Id = 8,
			ChannelPost = new Message
			{
				Chat = new Chat { Id = 400, Type = ChatType.Channel },
			},
		};

		Assert.Equal(400, UpdateLaneKey.FromUpdate(update));
	}

	[Fact]
	public void MyChatMember_UsesChatId()
	{
		var update = new Update
		{
			Id = 9,
			MyChatMember = new ChatMemberUpdated
			{
				Chat = new Chat { Id = 500 },
				From = new User { Id = 1, IsBot = false, FirstName = "u" },
				Date = DateTime.UtcNow,
				OldChatMember = new ChatMemberMember
				{
					User = new User { Id = 2, IsBot = true, FirstName = "b" },
				},
				NewChatMember = new ChatMemberAdministrator
				{
					User = new User { Id = 2, IsBot = true, FirstName = "b" },
					CanBeEdited = false,
					IsAnonymous = false,
					CanManageChat = true,
					CanDeleteMessages = false,
					CanManageVideoChats = false,
					CanRestrictMembers = false,
					CanPromoteMembers = false,
					CanChangeInfo = false,
					CanInviteUsers = false,
					CanPostStories = false,
					CanEditStories = false,
					CanDeleteStories = false,
				},
			},
		};

		Assert.Equal(500, UpdateLaneKey.FromUpdate(update));
	}

	[Fact]
	public void Empty_update_uses_global_lane()
	{
		Assert.Equal(UpdateLaneKey.Global, UpdateLaneKey.FromUpdate(new Update { Id = 10 }));
	}

	[Fact]
	public void Lane_key_is_stable_across_repeated_reads()
	{
		// Mutation-minded: pure function of update shape; no ambient mutation.
		var update = new Update
		{
			Id = 11,
			Message = new Message { Chat = new Chat { Id = 42 } },
		};

		for (var i = 0; i < 50; i++)
		{
			Assert.Equal(42, UpdateLaneKey.FromUpdate(update));
		}
	}
}
