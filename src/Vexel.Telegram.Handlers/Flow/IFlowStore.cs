namespace Vexel.Telegram.Handlers;

/// <summary>
/// Pluggable per-(chat, user) conversation state. Reads are non-destructive; only
/// <see cref="CompleteAsync"/> / expiry clears an entry. 2.0 ships
/// <see cref="MemoryFlowStore"/> only - draft payloads must be JSON-serializable POCOs
/// so a future redis store can swap in without changing call sites.
/// </summary>
public interface IFlowStore
{
	/// <summary>
	/// Non-destructive read of the armed entry for <paramref name="chatId"/> /
	/// <paramref name="userId"/>. Returns <see langword="null"/> when missing or expired
	/// (expired entries are removed as a side effect of the miss).
	/// </summary>
	/// <param name="chatId">Telegram chat id.</param>
	/// <param name="userId">Telegram user id.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The live entry, or <see langword="null"/>.</returns>
	ValueTask<FlowEntry?> GetAsync(
		long chatId,
		long userId,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Arms or replaces the entry for <paramref name="chatId"/> / <paramref name="userId"/>.
	/// </summary>
	/// <param name="chatId">Telegram chat id.</param>
	/// <param name="userId">Telegram user id.</param>
	/// <param name="entry">New entry (step key, draft, expiry).</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	ValueTask SetAsync(
		long chatId,
		long userId,
		FlowEntry entry,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Explicitly clears the entry (successful completion or cancel). No-op when missing.
	/// </summary>
	/// <param name="chatId">Telegram chat id.</param>
	/// <param name="userId">Telegram user id.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	ValueTask CompleteAsync(
		long chatId,
		long userId,
		CancellationToken cancellationToken = default);
}
