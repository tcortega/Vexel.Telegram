using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Vexel.Telegram.Handlers;

/// <summary>
/// In-process <see cref="IFlowStore"/>. Default 2.0 store; not durable across process restarts.
/// Expired entries are removed lazily on read and by an opportunistic sweep on write, so abandoned
/// flows (armed, then never answered) cannot accumulate for the lifetime of the process.
/// </summary>
/// <param name="timeProvider">Clock used for TTL checks; defaults to the system clock.</param>
/// <param name="sweepInterval">
/// Minimum interval between expiry sweeps; defaults to one minute. A sweep is amortized onto
/// <see cref="SetAsync"/> so the store owns no timer and no disposal contract.
/// </param>
public sealed class MemoryFlowStore(TimeProvider? timeProvider = null, TimeSpan? sweepInterval = null) : IFlowStore
{
	private readonly ConcurrentDictionary<FlowKey, FlowEntry> _entries = new();
	private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
	private readonly TimeSpan _sweepInterval = sweepInterval ?? TimeSpan.FromMinutes(1);
	private long _nextSweepTicks;
	private int _sweeping;

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
		SweepExpiredIfDue();
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

	/// <summary>
	/// Drops every expired entry. Exposed for tests; production sweeps are driven by
	/// <see cref="SetAsync"/> at most once per sweep interval.
	/// </summary>
	/// <returns>The number of entries removed.</returns>
	public int SweepExpired()
	{
		var now = _timeProvider.GetUtcNow();
		var removed = 0;

		foreach (var pair in _entries)
		{
			if (pair.Value.ExpiresAt <= now && _entries.TryRemove(pair))
			{
				// Value-matched removal: a concurrent re-arm replaces the entry and must survive.
				removed++;
			}
		}

		return removed;
	}

	private void SweepExpiredIfDue()
	{
		var now = _timeProvider.GetUtcNow();
		if (now.UtcTicks < Volatile.Read(ref _nextSweepTicks))
		{
			return;
		}

		if (Interlocked.Exchange(ref _sweeping, 1) == 1)
		{
			return;
		}

		try
		{
			Volatile.Write(ref _nextSweepTicks, (now + _sweepInterval).UtcTicks);
			_ = SweepExpired();
		}
		finally
		{
			Volatile.Write(ref _sweeping, 0);
		}
	}

	[StructLayout(LayoutKind.Auto)]
	private readonly record struct FlowKey(long ChatId, long UserId);
}
