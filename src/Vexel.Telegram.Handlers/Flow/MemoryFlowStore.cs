using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Vexel.Telegram.Handlers;

/// <summary>
/// In-process <see cref="IFlowStore"/>. Default 2.0 store; not durable across process restarts.
/// </summary>
/// <param name="timeProvider">Clock used for TTL checks; defaults to the system clock.</param>
public sealed class MemoryFlowStore(TimeProvider? timeProvider = null) : IFlowStore
{
	private readonly ConcurrentDictionary<FlowKey, FlowEntry> _entries = new();
	private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

	/// <inheritdoc />
	public ValueTask<FlowEntry?> GetAsync(
		long chatId,
		long userId,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var key = new FlowKey(chatId, userId);
		if (!_entries.TryGetValue(key, out var entry))
		{
			return ValueTask.FromResult<FlowEntry?>(null);
		}

		if (entry.ExpiresAt <= _timeProvider.GetUtcNow())
		{
			_ = _entries.TryRemove(key, out _);
			return ValueTask.FromResult<FlowEntry?>(null);
		}

		return ValueTask.FromResult<FlowEntry?>(entry);
	}

	/// <inheritdoc />
	public ValueTask SetAsync(
		long chatId,
		long userId,
		FlowEntry entry,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entry);
		cancellationToken.ThrowIfCancellationRequested();

		_entries[new FlowKey(chatId, userId)] = entry;
		return ValueTask.CompletedTask;
	}

	/// <inheritdoc />
	public ValueTask CompleteAsync(
		long chatId,
		long userId,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		_ = _entries.TryRemove(new FlowKey(chatId, userId), out _);
		return ValueTask.CompletedTask;
	}

	[StructLayout(LayoutKind.Auto)]
	private readonly record struct FlowKey(long ChatId, long UserId);
}
