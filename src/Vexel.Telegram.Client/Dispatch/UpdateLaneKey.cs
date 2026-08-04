using Telegram.Bot.Types;

namespace Vexel.Telegram.Client.Dispatch;

/// <summary>
/// Resolves the per-chat (or per-user) ordering lane key for an update.
/// </summary>
public static class UpdateLaneKey
{
	/// <summary>
	/// Shared lane used for chatless updates that are not keyed by a user id
	/// (for example polls without a chat context).
	/// </summary>
	public const long Global = long.MinValue;

	/// <summary>
	/// Computes the lane key for <paramref name="update"/>.
	/// </summary>
	/// <param name="update">The inbound Telegram update.</param>
	/// <returns>
	/// Message chat id; callback <c>Message?.Chat.Id ?? From.Id</c>;
	/// inline / chosen-inline <c>From.Id</c>; chat id when present on other kinds;
	/// otherwise <see cref="Global"/>.
	/// </returns>
	public static long FromUpdate(Update update)
	{
		ArgumentNullException.ThrowIfNull(update);

		if (update.Message?.Chat is { } messageChat)
		{
			return messageChat.Id;
		}

		if (update.EditedMessage?.Chat is { } editedChat)
		{
			return editedChat.Id;
		}

		if (update.ChannelPost?.Chat is { } channelChat)
		{
			return channelChat.Id;
		}

		if (update.EditedChannelPost?.Chat is { } editedChannelChat)
		{
			return editedChannelChat.Id;
		}

		if (update.CallbackQuery is { } callback)
		{
			return callback.Message?.Chat.Id ?? callback.From.Id;
		}

		if (update.InlineQuery is { } inlineQuery)
		{
			return inlineQuery.From.Id;
		}

		if (update.ChosenInlineResult is { } chosen)
		{
			return chosen.From.Id;
		}

		if (update.MyChatMember?.Chat is { } myChatMemberChat)
		{
			return myChatMemberChat.Id;
		}

		if (update.ChatMember?.Chat is { } chatMemberChat)
		{
			return chatMemberChat.Id;
		}

		if (update.ChatJoinRequest?.Chat is { } joinRequestChat)
		{
			return joinRequestChat.Id;
		}

		if (update.MessageReaction?.Chat is { } reactionChat)
		{
			return reactionChat.Id;
		}

		if (update.MessageReactionCount?.Chat is { } reactionCountChat)
		{
			return reactionCountChat.Id;
		}

		if (update.ChatBoost?.Chat is { } boostChat)
		{
			return boostChat.Id;
		}

		if (update.RemovedChatBoost?.Chat is { } removedBoostChat)
		{
			return removedBoostChat.Id;
		}

		if (update.BusinessMessage?.Chat is { } businessChat)
		{
			return businessChat.Id;
		}

		if (update.EditedBusinessMessage?.Chat is { } editedBusinessChat)
		{
			return editedBusinessChat.Id;
		}

		if (update.DeletedBusinessMessages?.Chat is { } deletedBusinessChat)
		{
			return deletedBusinessChat.Id;
		}

		return Global;
	}
}
