using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Vexel.Telegram.Client;
using Vexel.Telegram.Client.Dispatch;
using Vexel.Telegram.Client.Extensions;

namespace Vexel.Telegram.Tests.Dispatch;

public sealed class UpdateSchedulerTests
{
	[Fact]
	public async Task SameChat_UpdatesAreSerializedInOrder()
	{
		using var gate = new SemaphoreSlim(0, 1);
		var events = new ConcurrentQueue<string>();
		async Task dispatcher(Update update, CancellationToken ct)
		{
			events.Enqueue($"start:{update.Id}");
			if (update.Id == 1)
			{
				// Hold the first update so a second same-chat update can queue behind it.
				_ = await gate.WaitAsync(TimeSpan.FromSeconds(5), ct);
			}

			await Task.Delay(20, ct);
			events.Enqueue($"end:{update.Id}");
		}

		await using var scheduler = CreateScheduler(dispatcher);

		await scheduler.ScheduleAsync(MessageUpdate(1, chatId: 10), CancellationToken.None);
		await scheduler.ScheduleAsync(MessageUpdate(2, chatId: 10), CancellationToken.None);

		// First update is inside the handler; second must not have started yet.
		await WaitForAsync(() => events.Contains("start:1", StringComparer.Ordinal));
		await Task.Delay(50);
		Assert.DoesNotContain("start:2", events, StringComparer.Ordinal);

		_ = gate.Release();

		await WaitForAsync(() => events.Contains("end:2", StringComparer.Ordinal));

		Assert.Equal(
			["start:1", "end:1", "start:2", "end:2"],
			[.. events]);
	}

	[Fact]
	public async Task CrossChat_UpdatesRunInParallel()
	{
		var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var started = 0;

		async Task dispatcher(Update update, CancellationToken ct)
		{
			if (Interlocked.Increment(ref started) == 2)
			{
				_ = bothStarted.TrySetResult();
			}

			await release.Task.WaitAsync(ct);
		}

		await using var scheduler = CreateScheduler(dispatcher);

		await scheduler.ScheduleAsync(MessageUpdate(1, chatId: 1), CancellationToken.None);
		await scheduler.ScheduleAsync(MessageUpdate(2, chatId: 2), CancellationToken.None);

		var finished = await Task.WhenAny(bothStarted.Task, Task.Delay(TimeSpan.FromSeconds(5)));
		Assert.Same(bothStarted.Task, finished);

		_ = release.TrySetResult();
		await WaitForAsync(() => Volatile.Read(ref started) == 2 && scheduler.ActiveLaneCount == 0);
	}

	[Fact]
	public async Task FaultIsolation_ExceptionDoesNotKillLaneOrOtherChats()
	{
		var events = new ConcurrentQueue<string>();
		Task dispatcher(Update update, CancellationToken _)
		{
			events.Enqueue($"run:{update.Id}");
			if (update.Id == 1)
			{
				throw new InvalidOperationException("boom");
			}

			return Task.CompletedTask;
		}

		await using var scheduler = CreateScheduler(dispatcher);

		await scheduler.ScheduleAsync(MessageUpdate(1, chatId: 10), CancellationToken.None);
		await scheduler.ScheduleAsync(MessageUpdate(2, chatId: 10), CancellationToken.None);
		await scheduler.ScheduleAsync(MessageUpdate(3, chatId: 20), CancellationToken.None);

		await WaitForAsync(() => events.Count >= 3);

		Assert.Contains("run:1", events, StringComparer.Ordinal);
		Assert.Contains("run:2", events, StringComparer.Ordinal);
		Assert.Contains("run:3", events, StringComparer.Ordinal);
	}

	[Fact]
	public async Task SlowHandler_InOneChat_DoesNotBlockOtherChat()
	{
		var slowEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseSlow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var fastDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		async Task dispatcher(Update update, CancellationToken ct)
		{
			if (update.Message!.Chat.Id == 1)
			{
				_ = slowEntered.TrySetResult();
				await releaseSlow.Task.WaitAsync(ct);
				return;
			}

			_ = fastDone.TrySetResult();
		}

		await using var scheduler = CreateScheduler(dispatcher);

		await scheduler.ScheduleAsync(MessageUpdate(1, chatId: 1), CancellationToken.None);
		await slowEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

		await scheduler.ScheduleAsync(MessageUpdate(2, chatId: 2), CancellationToken.None);

		var finished = await Task.WhenAny(fastDone.Task, Task.Delay(TimeSpan.FromSeconds(5)));
		Assert.Same(fastDone.Task, finished);
		Assert.False(releaseSlow.Task.IsCompleted);

		_ = releaseSlow.TrySetResult();
		await WaitForAsync(() => scheduler.ActiveLaneCount == 0);
	}

	[Fact]
	public async Task Backpressure_WaitsWhenLaneIsFull()
	{
		var blockFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var inHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var processed = 0;

		async Task dispatcher(Update update, CancellationToken ct)
		{
			if (Interlocked.Increment(ref processed) == 1)
			{
				_ = inHandler.TrySetResult();
				await blockFirst.Task.WaitAsync(ct);
			}
		}

		await using var scheduler = CreateScheduler(dispatcher, laneCapacity: 1);

		await scheduler.ScheduleAsync(MessageUpdate(1, chatId: 5), CancellationToken.None);
		await inHandler.Task.WaitAsync(TimeSpan.FromSeconds(5));

		// Fills the single buffer slot while update 1 is still running.
		await scheduler.ScheduleAsync(MessageUpdate(2, chatId: 5), CancellationToken.None);

		var third = scheduler.ScheduleAsync(MessageUpdate(3, chatId: 5), CancellationToken.None).AsTask();

		// Third schedule must wait on backpressure while capacity is exhausted.
		var timeout = await Task.WhenAny(third, Task.Delay(TimeSpan.FromMilliseconds(100)));
		Assert.NotSame(third, timeout);
		Assert.False(third.IsCompleted);

		_ = blockFirst.TrySetResult();

		await third.WaitAsync(TimeSpan.FromSeconds(5));
		await WaitForAsync(() => Volatile.Read(ref processed) == 3);
	}

	[Fact]
	public async Task IdleLanes_AreEvicted()
	{
		static Task dispatcher(Update _1, CancellationToken _2) => Task.CompletedTask;
		await using var scheduler = CreateScheduler(dispatcher);

		await scheduler.ScheduleAsync(MessageUpdate(1, chatId: 99), CancellationToken.None);
		await WaitForAsync(() => scheduler.ActiveLaneCount == 0);
	}

	[Fact]
	public async Task Dispose_DrainsBufferedUpdates()
	{
		var processed = 0;
		async Task dispatcher(Update update, CancellationToken ct)
		{
			await Task.Delay(20, ct);
			_ = Interlocked.Increment(ref processed);
		}

		var scheduler = CreateScheduler(dispatcher);

		for (var id = 1; id <= 5; id++)
		{
			await scheduler.ScheduleAsync(MessageUpdate(id, chatId: 7), CancellationToken.None);
		}

		await scheduler.DisposeAsync();

		Assert.Equal(5, Volatile.Read(ref processed));
	}

	[Fact]
	public async Task StopAsync_DrainsBufferedUpdatesAndLeavesSchedulerUsable()
	{
		var processed = new ConcurrentQueue<int>();
		async Task dispatcher(Update update, CancellationToken ct)
		{
			await Task.Delay(20, ct);
			processed.Enqueue(update.Id);
		}

		await using var scheduler = CreateScheduler(dispatcher);

		for (var id = 1; id <= 5; id++)
		{
			await scheduler.ScheduleAsync(MessageUpdate(id, chatId: 7), CancellationToken.None);
		}

		await scheduler.StopAsync();

		Assert.Equal([1, 2, 3, 4, 5], [.. processed]);

		await scheduler.ScheduleAsync(MessageUpdate(6, chatId: 7), CancellationToken.None);
		await scheduler.StopAsync();

		Assert.Equal([1, 2, 3, 4, 5, 6], [.. processed]);
	}

	[Fact]
	public async Task StopAsync_DispatchesBufferedUpdatesWhileContainerIsAlive()
	{
		var handled = new ConcurrentQueue<int>();
		var services = new ServiceCollection();
		_ = services.AddSingleton(handled);
		_ = services.AddSingleton(Options.Create(new VexelClientOptions()));
		_ = services.AddSingleton<ILogger<UpdateDispatcher>>(NullLogger<UpdateDispatcher>.Instance);
		_ = services.AddSingleton<ILogger<UpdateScheduler>>(NullLogger<UpdateScheduler>.Instance);
		_ = services.AddRawUpdateHandler<QueueingRawHandler>();
		_ = services.AddSingleton(static sp => new UpdateDispatcher(
			sp.GetRequiredService<IServiceScopeFactory>(),
			sp.GetRequiredService<RawUpdateHandlerRegistry>(),
			sp.GetServices<IUpdateCompletionHook>(),
			sp.GetRequiredService<IOptions<VexelClientOptions>>(),
			sp.GetRequiredService<ILogger<UpdateDispatcher>>(),
			sp.GetService<IUpdateRouter>()));
		_ = services.AddSingleton<UpdateScheduler>();

		await using var provider = services.BuildServiceProvider();
		var scheduler = provider.GetRequiredService<UpdateScheduler>();

		for (var id = 1; id <= 3; id++)
		{
			await scheduler.ScheduleAsync(MessageUpdate(id, chatId: 42), CancellationToken.None);
		}

		await scheduler.StopAsync();

		Assert.Equal([1, 2, 3], [.. handled]);
	}

	[Fact]
	public async Task Dispose_IsIdempotentAndRejectsFurtherScheduling()
	{
		var scheduler = CreateScheduler((_, _) => Task.CompletedTask);

		await Task.WhenAll(
			scheduler.DisposeAsync().AsTask(),
			scheduler.DisposeAsync().AsTask());

		_ = await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
			await scheduler.ScheduleAsync(MessageUpdate(1, chatId: 1), CancellationToken.None));
	}

	[Fact]
	public async Task Per_chat_order_holds_under_interleaved_multi_chat_schedule()
	{
		// Property-minded: for every chat, completion order equals schedule order even when
		// many chats are interleaved and handlers yield.
		var perChat = new ConcurrentDictionary<long, ConcurrentQueue<int>>();
		async Task dispatcher(Update update, CancellationToken ct)
		{
			_ = ct;
			await Task.Yield();
			var chatId = update.Message!.Chat.Id;
			var queue = perChat.GetOrAdd(chatId, static _ => new ConcurrentQueue<int>());
			queue.Enqueue(update.Id);
		}

		await using var scheduler = CreateScheduler(dispatcher);

		const int chats = 8;
		const int perChatCount = 12;
		var expected = new Dictionary<long, List<int>>();
		var id = 1;
		for (var n = 0; n < perChatCount; n++)
		{
			for (var chat = 1; chat <= chats; chat++)
			{
				var chatId = (long)chat;
				if (!expected.TryGetValue(chatId, out var list))
				{
					list = [];
					expected[chatId] = list;
				}

				list.Add(id);
				await scheduler.ScheduleAsync(MessageUpdate(id, chatId), CancellationToken.None);
				id++;
			}
		}

		await WaitForAsync(() =>
			perChat.Count == chats
			&& perChat.Values.All(static q => q.Count == perChatCount)
			&& scheduler.ActiveLaneCount == 0);

		foreach (var (chatId, expectedIds) in expected)
		{
			Assert.Equal(expectedIds, [.. perChat[chatId]]);
		}
	}

	[Fact]
	public async Task Dispatcher_IsolatesRawHandlerFaults()
	{
		var tracker = new HandlerTracker();
		var services = new ServiceCollection();
		_ = services.AddSingleton(tracker);
		_ = services.AddRawUpdateHandler<ThrowingRawHandler>();
		_ = services.AddRawUpdateHandler<TrackedRawHandler>();

		await using var provider = services.BuildServiceProvider(validateScopes: true);
		var dispatcher = CreateDispatcher(provider);

		await dispatcher.DispatchAsync(MessageUpdate(1, chatId: 1), CancellationToken.None);

		Assert.Equal(1, tracker.Handled);
	}

	[Fact]
	public async Task Dispatcher_ResolvesScopedHandlersPerUpdate()
	{
		var tracker = new HandlerTracker();
		var services = new ServiceCollection();
		_ = services.AddSingleton(tracker);
		_ = services.AddRawUpdateHandler<TrackedRawHandler>();

		await using var provider = services.BuildServiceProvider(validateScopes: true);
		var dispatcher = CreateDispatcher(provider);

		await dispatcher.DispatchAsync(MessageUpdate(1, chatId: 1), CancellationToken.None);
		await dispatcher.DispatchAsync(MessageUpdate(2, chatId: 1), CancellationToken.None);

		Assert.Equal(2, tracker.Created);
		Assert.Equal(2, tracker.Disposed);
	}

	[Fact]
	public async Task Dispatcher_IsolatesHandlerResolutionFaults()
	{
		var services = new ServiceCollection();
		_ = services.AddRawUpdateHandler<ThrowingConstructorRawHandler>();

		await using var provider = services.BuildServiceProvider(validateScopes: true);
		var dispatcher = CreateDispatcher(provider);

		var exception = await Record.ExceptionAsync(() =>
			dispatcher.DispatchAsync(MessageUpdate(1, chatId: 1), CancellationToken.None));

		Assert.Null(exception);
	}

	[Fact]
	public async Task Dispatcher_RunsHealthyHandlerWhenAnotherFailsToConstruct()
	{
		var tracker = new HandlerTracker();
		var services = new ServiceCollection();
		_ = services.AddSingleton(tracker);
		_ = services.AddRawUpdateHandler<ThrowingConstructorRawHandler>();
		_ = services.AddRawUpdateHandler<TrackedRawHandler>();

		await using var provider = services.BuildServiceProvider(validateScopes: true);
		var dispatcher = CreateDispatcher(provider);

		await dispatcher.DispatchAsync(MessageUpdate(1, chatId: 1), CancellationToken.None);

		Assert.Equal(1, tracker.Created);
		Assert.Equal(1, tracker.Handled);
		Assert.Equal(1, tracker.Disposed);
	}

	[Fact]
	public async Task Dispatcher_IgnoresContainerRegisteredRawHandlers()
	{
		// The container-set path is gone: AddRawUpdateHandler<T> is the only registration that
		// makes a raw handler run, so a hand-rolled IRawUpdateHandler service is never dispatched.
		var tracker = new HandlerTracker();
		var services = new ServiceCollection();
		_ = services.AddSingleton(tracker);
		_ = services.AddScoped<IRawUpdateHandler, TrackedRawHandler>();
		_ = services.AddSingleton<IRawUpdateHandler, TrackedRawHandler>();

		await using var provider = services.BuildServiceProvider(validateScopes: true);
		var dispatcher = CreateDispatcher(provider);

		await dispatcher.DispatchAsync(MessageUpdate(1, chatId: 1), CancellationToken.None);

		Assert.Equal(0, tracker.Handled);
	}

	[Fact]
	public async Task StopAsync_WindsDownWhenShutdownBudgetExpires()
	{
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		async Task dispatcher(Update update, CancellationToken ct)
		{
			_ = entered.TrySetResult();
			try
			{
				await Task.Delay(Timeout.Infinite, ct);
			}
			catch (OperationCanceledException)
			{
				_ = cancelled.TrySetResult();
				throw;
			}
		}

		await using var scheduler = CreateScheduler(dispatcher);

		await scheduler.ScheduleAsync(MessageUpdate(1, chatId: 3), CancellationToken.None);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

		using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
		var stop = scheduler.StopAsync(budget.Token);

		await stop.WaitAsync(TimeSpan.FromSeconds(5));
		await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}

	private static UpdateDispatcher CreateDispatcher(IServiceProvider provider) =>
		new(
			provider.GetRequiredService<IServiceScopeFactory>(),
			provider.GetService<RawUpdateHandlerRegistry>() ?? new RawUpdateHandlerRegistry(),
			provider.GetServices<IUpdateCompletionHook>(),
			Options.Create(new VexelClientOptions()),
			NullLogger<UpdateDispatcher>.Instance,
			provider.GetService<IUpdateRouter>());

	private static UpdateScheduler CreateScheduler(
		Func<Update, CancellationToken, Task> dispatch,
		int laneCapacity = 64)
	{
		var options = Options.Create(new VexelClientOptions { LaneCapacity = laneCapacity });
		return new UpdateScheduler(dispatch, options, NullLogger<UpdateScheduler>.Instance);
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

	private static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
	{
		var limit = timeout ?? TimeSpan.FromSeconds(5);
		var start = DateTime.UtcNow;
		while (!condition())
		{
			if (DateTime.UtcNow - start > limit)
			{
				throw new TimeoutException("Condition was not met within the allotted time.");
			}

			await Task.Delay(10);
		}
	}

	private sealed class QueueingRawHandler(ConcurrentQueue<int> handled) : IRawUpdateHandler
	{
		public Task HandleAsync(Update update, CancellationToken cancellationToken)
		{
			handled.Enqueue(update.Id);
			return Task.CompletedTask;
		}
	}

	private sealed class ThrowingRawHandler : IRawUpdateHandler
	{
		public Task HandleAsync(Update update, CancellationToken cancellationToken) =>
			throw new InvalidOperationException("raw boom");
	}

	private sealed class ThrowingConstructorRawHandler : IRawUpdateHandler
	{
		public ThrowingConstructorRawHandler() =>
			throw new InvalidOperationException("handler construction boom");

		public Task HandleAsync(Update update, CancellationToken cancellationToken) => Task.CompletedTask;
	}

	private sealed class HandlerTracker
	{
		private int _created;
		private int _handled;
		private int _disposed;

		public int Created => Volatile.Read(ref _created);

		public int Handled => Volatile.Read(ref _handled);

		public int Disposed => Volatile.Read(ref _disposed);

		public void MarkCreated() => _ = Interlocked.Increment(ref _created);

		public void MarkHandled() => _ = Interlocked.Increment(ref _handled);

		public void MarkDisposed() => _ = Interlocked.Increment(ref _disposed);
	}

	private sealed class TrackedRawHandler : IRawUpdateHandler, IDisposable
	{
		private readonly HandlerTracker _tracker;

		public TrackedRawHandler(HandlerTracker tracker)
		{
			_tracker = tracker;
			tracker.MarkCreated();
		}

		public Task HandleAsync(Update update, CancellationToken cancellationToken)
		{
			_tracker.MarkHandled();
			return Task.CompletedTask;
		}

		public void Dispose() => _tracker.MarkDisposed();
	}
}
