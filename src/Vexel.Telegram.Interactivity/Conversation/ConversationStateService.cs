using Microsoft.Extensions.Caching.Memory;
using Remora.Results;

namespace Vexel.Telegram.Interactivity.Conversation;

/// <inheritdoc />
public class ConversationStateService(IMemoryCache cache) : IConversationStateService
{
	private static readonly TimeSpan s_defaultExpiry = TimeSpan.FromMinutes(5);

	/// <inheritdoc />
	public void SetAwaitingInput(long chatId, long userId, string handlerName, string? state = null, TimeSpan? expiry = null)
	{
		var key = GetCacheKey(chatId, userId);
		var options = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiry ?? s_defaultExpiry };

		var conversationState = new ConversationState { HandlerName = handlerName, State = state, };
		_ = cache.Set(key, conversationState, options);
	}

	/// <inheritdoc />
	public Result<ConversationState> TryGetAwaitingInput(long chatId, long userId)
	{
		var key = GetCacheKey(chatId, userId);
		if (!cache.TryGetValue<ConversationState>(key, out var conversationState))
		{
			return new NotFoundError();
		}

		cache.Remove(key);
		return conversationState;
	}

	/// <summary>
	/// Builds the key used for caching purposes only, is not related to the
	/// resolved handler ID & path. 
	/// </summary>
	/// <param name="chatId">The chat ID.</param>
	/// <param name="userId">The user ID.</param>
	/// <returns>The cache key.</returns>
	private static string GetCacheKey(long chatId, long userId)
		=> $"vexel::convo::{chatId}::{userId}";
}
