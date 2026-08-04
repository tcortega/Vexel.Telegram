using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;

namespace Vexel.Telegram.Client.Dispatch;

/// <summary>
/// Schedules updates onto ordered-per-chat lanes with cross-chat parallelism,
/// bounded await-backpressure, and idle-lane eviction.
/// </summary>
public sealed class UpdateScheduler : IAsyncDisposable
{
	private static readonly TimeSpan s_drainGracePeriod = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan s_forcedShutdownGracePeriod = TimeSpan.FromSeconds(2);

	private readonly IUpdateDispatcher _dispatcher;
	private readonly ILogger<UpdateScheduler> _logger;
	private readonly int _laneCapacity;
#if NET9_0_OR_GREATER
	private readonly Lock _gate = new();
#else
	private readonly object _gate = new();
#endif
	private readonly Dictionary<long, Lane> _lanes = [];
	private readonly List<Task> _workers = [];
	private CancellationTokenSource _shutdownCts = new();
	private int _disposeState;

	/// <summary>
	/// Initializes a new scheduler.
	/// </summary>
	/// <param name="dispatcher">Pipeline invoked for each dequeued update.</param>
	/// <param name="options">Client options (lane capacity, timeouts).</param>
	/// <param name="logger">Logger for dispatch faults.</param>
	public UpdateScheduler(
		IUpdateDispatcher dispatcher,
		IOptions<VexelClientOptions> options,
		ILogger<UpdateScheduler> logger)
	{
		ArgumentNullException.ThrowIfNull(dispatcher);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(logger);

		_dispatcher = dispatcher;
		_logger = logger;

		var capacity = options.Value.LaneCapacity;
		if (capacity < 1)
		{
			throw new ArgumentOutOfRangeException(
				nameof(options),
				capacity,
				"LaneCapacity must be at least 1.");
		}

		_laneCapacity = capacity;
	}

	/// <summary>
	/// Number of live lanes. Used by tests and diagnostics.
	/// </summary>
	public int ActiveLaneCount
	{
		get
		{
			lock (_gate)
			{
				return _lanes.Count;
			}
		}
	}

	private bool IsDisposed => Volatile.Read(ref _disposeState) != 0;

	/// <summary>
	/// Enqueues <paramref name="update"/> on its lane.
	/// Awaits when the lane is at capacity (backpressure); never drops.
	/// Does not wait for the update to finish processing.
	/// </summary>
	/// <param name="update">The inbound Telegram update.</param>
	/// <param name="cancellationToken">Token that cancels the enqueue wait.</param>
	public async ValueTask ScheduleAsync(Update update, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(update);
		ObjectDisposedException.ThrowIf(IsDisposed, this);

		var key = UpdateLaneKey.FromUpdate(update);

		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var lane = RentLane(key);

			try
			{
				await lane.Writer.WriteAsync(update, cancellationToken).ConfigureAwait(false);
				return;
			}
			catch (ChannelClosedException)
			{
				// Lane retired between rent and write; drop the rent and retry on a fresh lane.
				ReleaseFailedRent(key, lane);
			}
			catch (OperationCanceledException)
			{
				ReleaseFailedRent(key, lane);
				throw;
			}
		}
	}

	/// <summary>
	/// Completes all lanes and waits for in-flight workers to finish, so buffered updates are
	/// dispatched while their dependencies are still alive. Workers are only cancelled if the
	/// drain does not finish within the grace period. The scheduler stays usable afterwards.
	/// </summary>
	public Task StopAsync() => DrainAsync(disposing: false);

	/// <summary>
	/// Drains like <see cref="StopAsync"/> and then releases the scheduler. Callers hosted in a
	/// container should stop the scheduler before the container tears down its services.
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposeState, 1) != 0)
		{
			return;
		}

		await DrainAsync(disposing: true).ConfigureAwait(false);
	}

	private async Task DrainAsync(bool disposing)
	{
		List<Task> workers;
		CancellationTokenSource shutdownCts;
		lock (_gate)
		{
			shutdownCts = _shutdownCts;
			foreach (var lane in _lanes.Values)
			{
				_ = lane.Writer.TryComplete();
			}

			workers = [.. _workers];
			_lanes.Clear();
		}

		var drain = Task.WhenAll(workers);
		var workersAbandoned = false;
		try
		{
			await drain.WaitAsync(s_drainGracePeriod).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			_logger.LogWarning(
				"Update lanes did not drain within {GracePeriod}; cancelling in-flight dispatch",
				s_drainGracePeriod);

			await shutdownCts.CancelAsync().ConfigureAwait(false);

			try
			{
				await drain.WaitAsync(s_forcedShutdownGracePeriod).ConfigureAwait(false);
			}
			catch (TimeoutException)
			{
				// Last resort: abandon workers that ignore cancellation.
				workersAbandoned = true;
			}
			catch (OperationCanceledException)
			{
			}
		}
		catch (OperationCanceledException)
		{
			// A worker observed cancellation from elsewhere; nothing left to drain.
		}

		var retired = false;
		lock (_gate)
		{
			if (shutdownCts.IsCancellationRequested && ReferenceEquals(_shutdownCts, shutdownCts) && !IsDisposed)
			{
				// Future lanes must not start on an already-cancelled token.
				_shutdownCts = new CancellationTokenSource();
				retired = true;
			}
		}

		// Abandoned workers still observe the token, so its source must outlive them.
		if (!workersAbandoned && (disposing || retired))
		{
			shutdownCts.Dispose();
		}
	}

	private Lane RentLane(long key)
	{
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(IsDisposed, this);

			if (_lanes.TryGetValue(key, out var existing))
			{
				existing.Pending++;
				return existing;
			}

			var lane = new Lane(_laneCapacity)
			{
				Pending = 1,
			};
			_lanes[key] = lane;

			var worker = ProcessLaneAsync(key, lane, _shutdownCts.Token);
			_workers.Add(worker);
			_ = worker.ContinueWith(
				static (t, state) =>
				{
					var scheduler = (UpdateScheduler)state!;
					lock (scheduler._gate)
					{
						_ = scheduler._workers.Remove(t);
					}
				},
				this,
				CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);

			return lane;
		}
	}

	private void ReleaseFailedRent(long key, Lane lane)
	{
		lock (_gate)
		{
			lane.Pending--;
			if (lane.Pending == 0)
			{
				_ = lane.Writer.TryComplete();
				if (_lanes.TryGetValue(key, out var current) && ReferenceEquals(current, lane))
				{
					_ = _lanes.Remove(key);
				}
			}
		}
	}

	private async Task ProcessLaneAsync(long key, Lane lane, CancellationToken shutdownToken)
	{
		try
		{
			await foreach (var update in lane.Reader.ReadAllAsync(shutdownToken).ConfigureAwait(false))
			{
				await DispatchIsolatedAsync(update, key, shutdownToken).ConfigureAwait(false);

				lock (_gate)
				{
					lane.Pending--;
					if (lane.Pending == 0)
					{
						// Idle eviction: retire the lane so count tracks active chats, not lifetime chats.
						_ = lane.Writer.TryComplete();
						if (_lanes.TryGetValue(key, out var current) && ReferenceEquals(current, lane))
						{
							_ = _lanes.Remove(key);
						}
					}
				}
			}
		}
		catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
		{
			// Normal shutdown.
		}
		catch (Exception ex)
		{
			// A lane worker must never die silently without cleanup; log and retire the lane.
			_logger.LogError(ex, "Update lane {LaneKey} terminated unexpectedly", key);
			lock (_gate)
			{
				_ = lane.Writer.TryComplete(ex);
				if (_lanes.TryGetValue(key, out var current) && ReferenceEquals(current, lane))
				{
					_ = _lanes.Remove(key);
				}
			}
		}
	}

	private async Task DispatchIsolatedAsync(Update update, long key, CancellationToken cancellationToken)
	{
		try
		{
			await _dispatcher.DispatchAsync(update, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Shutdown mid-dispatch.
		}
		catch (Exception ex)
		{
			// Last-resort isolation if a custom dispatcher lets exceptions escape.
			_logger.LogError(
				ex,
				"Dispatch failed for update {UpdateId} on lane {LaneKey}",
				update.Id,
				key);
		}
	}

	private sealed class Lane
	{
		private readonly Channel<Update> _channel;

		public Lane(int capacity)
		{
			_channel = Channel.CreateBounded<Update>(new BoundedChannelOptions(capacity)
			{
				FullMode = BoundedChannelFullMode.Wait,
				SingleReader = true,
				SingleWriter = false,
				AllowSynchronousContinuations = false,
			});
		}

		public ChannelWriter<Update> Writer => _channel.Writer;

		public ChannelReader<Update> Reader => _channel.Reader;

		/// <summary>
		/// Items scheduled onto this lane that have not finished processing.
		/// Mutated only under the scheduler gate.
		/// </summary>
		public int Pending { get; set; }
	}
}
