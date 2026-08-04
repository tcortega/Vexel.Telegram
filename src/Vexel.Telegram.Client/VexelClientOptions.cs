namespace Vexel.Telegram.Client;

/// <summary>
/// Configuration options for <see cref="VexelClient"/> and its update scheduler.
/// </summary>
public sealed class VexelClientOptions
{
	/// <summary>
	/// Default bounded capacity for each per-chat scheduling lane.
	/// </summary>
	public const int DefaultLaneCapacity = 64;

	/// <summary>
	/// When <see langword="true"/>, pending updates are dropped on start.
	/// Defaults to <see langword="true"/>.
	/// </summary>
	public bool DropPendingUpdates { get; set; } = true;

	/// <summary>
	/// Bounded capacity of each per-chat (or per-user) scheduling lane.
	/// When a lane is full, scheduling awaits capacity rather than dropping updates.
	/// Defaults to <see cref="DefaultLaneCapacity"/>.
	/// </summary>
	public int LaneCapacity { get; set; } = DefaultLaneCapacity;

	/// <summary>
	/// Optional timeout applied around each dispatch stage (routed / On* / raw handler).
	/// Defaults to <see langword="null"/> (disabled).
	/// Prefer leaving this off: a timeout that cancels mid-Telegram API call is usually
	/// worse than a temporarily stuck lane. Power users may opt in deliberately.
	/// </summary>
	public TimeSpan? HandlerTimeout { get; set; }
}
