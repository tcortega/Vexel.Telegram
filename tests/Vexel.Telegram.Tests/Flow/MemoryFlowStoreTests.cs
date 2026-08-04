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

	private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
	{
		private DateTimeOffset _utcNow = start;

		public override DateTimeOffset GetUtcNow() => _utcNow;

		public void Advance(TimeSpan delta) => _utcNow += delta;
	}
}
