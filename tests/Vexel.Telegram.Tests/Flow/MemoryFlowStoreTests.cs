using Vexel.Telegram.Handlers;

namespace Vexel.Telegram.Tests.Flows;

public sealed class MemoryFlowStoreTests
{
	[Fact]
	public async Task Get_is_non_destructive()
	{
		var store = new MemoryFlowStore();
		var entry = new FlowEntry("Demo.Step", DraftJson: null, DateTimeOffset.UtcNow.AddMinutes(15));
		await store.SetAsync(1, 2, entry);

		var first = await store.GetAsync(1, 2);
		var second = await store.GetAsync(1, 2);

		Assert.NotNull(first);
		Assert.NotNull(second);
		Assert.Equal("Demo.Step", second.StepKey);
	}

	[Fact]
	public async Task Complete_removes_entry()
	{
		var store = new MemoryFlowStore();
		await store.SetAsync(1, 2, new FlowEntry("Demo.Step", DraftJson: null, DateTimeOffset.UtcNow.AddMinutes(15)));

		await store.CompleteAsync(1, 2);

		Assert.Null(await store.GetAsync(1, 2));
	}

	[Fact]
	public async Task Expired_entry_is_cleared_on_read()
	{
		var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
		var store = new MemoryFlowStore(time);
		await store.SetAsync(
			1,
			2,
			new FlowEntry("Demo.Step", DraftJson: null, time.GetUtcNow().AddMinutes(15)));

		time.Advance(TimeSpan.FromMinutes(16));

		Assert.Null(await store.GetAsync(1, 2));
		// Second read stays a miss (entry was removed).
		Assert.Null(await store.GetAsync(1, 2));
	}

	[Fact]
	public async Task Abandoned_entries_are_swept_by_later_writes()
	{
		var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
		var store = new MemoryFlowStore(time, sweepInterval: TimeSpan.FromMinutes(1));

		// 100 users arm a step and never answer: nothing ever reads their keys again.
		for (var user = 0; user < 100; user++)
		{
			await store.SetAsync(1, user, new FlowEntry("Demo.Step", DraftJson: null, time.GetUtcNow().AddMinutes(15)));
		}

		time.Advance(TimeSpan.FromMinutes(16));

		// One unrelated write past the sweep interval evicts every abandoned entry.
		await store.SetAsync(2, 500, new FlowEntry("Demo.Step", DraftJson: null, time.GetUtcNow().AddMinutes(15)));

		Assert.Equal(0, store.SweepExpired());
		for (var user = 0; user < 100; user++)
		{
			Assert.Null(await store.GetAsync(1, user));
		}

		Assert.NotNull(await store.GetAsync(2, 500));
	}

	[Fact]
	public async Task Sweep_keeps_entries_rearmed_after_expiry_check()
	{
		var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
		var store = new MemoryFlowStore(time, sweepInterval: TimeSpan.FromMinutes(1));
		await store.SetAsync(1, 2, new FlowEntry("Demo.Step", DraftJson: null, time.GetUtcNow().AddMinutes(15)));

		time.Advance(TimeSpan.FromMinutes(16));
		await store.SetAsync(1, 2, new FlowEntry("Demo.Next", DraftJson: null, time.GetUtcNow().AddMinutes(15)));

		var entry = await store.GetAsync(1, 2);
		Assert.NotNull(entry);
		Assert.Equal("Demo.Next", entry.StepKey);
	}

	private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
	{
		private DateTimeOffset _utcNow = start;

		public override DateTimeOffset GetUtcNow() => _utcNow;

		public void Advance(TimeSpan delta) => _utcNow += delta;
	}
}
