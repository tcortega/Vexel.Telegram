namespace Vexel.Telegram.Client;

/// <summary>
/// Configuration options for <see cref="VexelClient"/>.
/// </summary>
public sealed class VexelClientOptions
{
	/// <summary>
	/// When <see langword="true"/>, pending updates are dropped on start.
	/// Defaults to <see langword="true"/>.
	/// </summary>
	public bool DropPendingUpdates { get; set; } = true;
}
