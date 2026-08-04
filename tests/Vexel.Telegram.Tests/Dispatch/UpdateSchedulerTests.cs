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
		var dispatcher = new ScriptedDispatcher(async (update, ct) =>
		{
			events.Enqueue($"start:{update.Id}");
			if (update.Id == 1)
			{
				// Hold the first update so a second same-chat update can queue behind it.
				_ = await gate.WaitAsync(TimeSpan.FromSeconds(5), ct);
			}

			await Task.Delay(20, ct);
			events.Enqueue($"end:{update.Id}");
		});

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

		var dispatcher = new ScriptedDispatcher(async (update, ct) =>
		{
			if (Interlocked.Increment(ref started) == 2)
			{
				_ = bothStarted.TrySetResult();
			}

			await release.Task.WaitAsync(ct);
		});

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
		var dispatcher = new ScriptedDispatcher((update, _) =>
		{
			events.Enqueue($"run:{update.Id}");
			if (update.Id == 1)
			{
				throw new InvalidOperationException("boom");
			}

			return Task.CompletedTask;
		});

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

		var dispatcher = new ScriptedDispatcher(async (update, ct) =>
		{
			if (update.Message!.Chat.Id == 1)
			{
				_ = slowEntered.TrySetResult();
				await releaseSlow.Task.WaitAsync(ct);
				return;
			}

			_ = fastDone.TrySetResult();
		});

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

		var dispatcher = new ScriptedDispatcher(async (update, ct) =>
		{
			if (Interlocked.Increment(ref processed) == 1)
			{
				_ = inHandler.TrySetResult();
				await blockFirst.Task.WaitAsync(ct);
			}
		});

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
		var dispatcher = new ScriptedDispatcher((_, _) => Task.CompletedTask);
		await using var scheduler = CreateScheduler(dispatcher);

		await scheduler.ScheduleAsync(MessageUpdate(1, chatId: 99), CancellationToken.None);
		await WaitForAsync(() => scheduler.ActiveLaneCount == 0);
	}

	[Fact]
	public async Task Dispose_DrainsBufferedUpdates()
	{
		var processed = 0;
		var dispatcher = new ScriptedDispatcher(async (update, ct) =>
		{
			await Task.Delay(20, ct);
			_ = Interlocked.Increment(ref processed);
		});

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
		var dispatcher = new ScriptedDispatcher(async (update, ct) =>
		{
			await Task.Delay(20, ct);
			processed.Enqueue(update.Id);
		});

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
		_ = services.AddSingleton<IRawUpdateHandler>(new DelegateRawHandler((update, _) =>
		{
			handled.Enqueue(update.Id);
			return Task.CompletedTask;
		}));
		_ = services.AddSingleton(Options.Create(new VexelClientOptions()));
		_ = services.AddSingleton(new RawUpdateHandlerRegistry());
		_ = services.AddSingleton<ILogger<UpdateDispatcher>>(NullLogger<UpdateDispatcher>.Instance);
		_ = services.AddSingleton<ILogger<UpdateScheduler>>(NullLogger<UpdateScheduler>.Instance);
		_ = services.AddSingleton<IUpdateDispatcher, UpdateDispatcher>();
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
		var scheduler = CreateScheduler(new ScriptedDispatcher((_, _) => Task.CompletedTask));

		await Task.WhenAll(
			scheduler.DisposeAsync().AsTask(),
			scheduler.DisposeAsync().AsTask());

		_ = await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
			await scheduler.ScheduleAsync(MessageUpdate(1, chatId: 1), CancellationToken.None));
	}

	[Fact]
	public async Task Dispatcher_IsolatesRawHandlerFaults()
	{
		var goodRan = false;
		var services = new ServiceCollection();
		_ = services.AddSingleton<IRawUpdateHandler>(
			new DelegateRawHandler((_, _) => throw new InvalidOperationException("raw boom")));
		_ = services.AddSingleton<IRawUpdateHandler>(new DelegateRawHandler((_, _) =>
		{
			goodRan = true;
			return Task.CompletedTask;
		}));

		await using var provider = services.BuildServiceProvider();
		await using var dispatcher = CreateDispatcher(provider);

		await dispatcher.DispatchAsync(MessageUpdate(1, chatId: 1), CancellationToken.None);

		Assert.True(goodRan);
	}

	[Fact]
	public async Task Dispatcher_ResolvesScopedHandlersPerUpdate()
	{
		var tracker = new HandlerTracker();
		var services = new ServiceCollection();
		_ = services.AddSingleton(tracker);
		_ = services.AddScoped<IRawUpdateHandler, TrackedRawHandler>();

		await using var provider = services.BuildServiceProvider(validateScopes: true);
		await using var dispatcher = CreateDispatcher(provider);

		await dispatcher.DispatchAsync(MessageUpdate(1, chatId: 1), CancellationToken.None);
		await dispatcher.DispatchAsync(MessageUpdate(2, chatId: 1), CancellationToken.None);

		Assert.Equal(2, tracker.Created);
		Assert.Equal(2, tracker.Disposed);
	}

	[Fact]
	public async Task Dispatcher_IsolatesHandlerResolutionFaults()
	{
		var services = new ServiceCollection();
		_ = services.AddScoped<IRawUpdateHandler, ThrowingConstructorRawHandler>();

		await using var provider = services.BuildServiceProvider(validateScopes: true);
		await using var dispatcher = CreateDispatcher(provider);

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
		await using var dispatcher = CreateDispatcher(provider);

		await dispatcher.DispatchAsync(MessageUpdate(1, chatId: 1), CancellationToken.None);

		Assert.Equal(1, tracker.Created);
		Assert.Equal(1, tracker.Handled);
		Assert.Equal(1, tracker.Disposed);
	}

	[Fact]
	public async Task Dispatcher_RunsDualRegisteredHandlerOnce()
	{
		var tracker = new HandlerTracker();
		var services = new ServiceCollection();
		_ = services.AddSingleton(tracker);
		_ = services.AddScoped<IRawUpdateHandler, TrackedRawHandler>();
		_ = services.AddRawUpdateHandler<TrackedRawHandler>();

		await using var provider = services.BuildServiceProvider(validateScopes: true);
		await using var dispatcher = CreateDispatcher(provider);

		await dispatcher.DispatchAsync(MessageUpdate(1, chatId: 1), CancellationToken.None);

		Assert.Equal(1, tracker.Handled);
	}

	[Fact]
	public async Task StopAsync_WindsDownWhenShutdownBudgetExpires()
	{
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var dispatcher = new ScriptedDispatcher(async (update, ct) =>
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
		});

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
			provider,
			provider.GetService<RawUpdateHandlerRegistry>() ?? new RawUpdateHandlerRegistry(),
			Options.Create(new VexelClientOptions()),
			NullLogger<UpdateDispatcher>.Instance);

	private static UpdateScheduler CreateScheduler(IUpdateDispatcher dispatcher, int laneCapacity = 64)
	{
		var options = Options.Create(new VexelClientOptions { LaneCapacity = laneCapacity });
		return new UpdateScheduler(dispatcher, options, NullLogger<UpdateScheduler>.Instance);
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

	private sealed class ScriptedDispatcher(Func<Update, CancellationToken, Task> handler) : IUpdateDispatcher
	{
		public Task DispatchAsync(Update update, CancellationToken cancellationToken) =>
			handler(update, cancellationToken);
	}

	private sealed class DelegateRawHandler(Func<Update, CancellationToken, Task> handler) : IRawUpdateHandler
	{
		public Task HandleAsync(Update update, CancellationToken cancellationToken) =>
			handler(update, cancellationToken);
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
