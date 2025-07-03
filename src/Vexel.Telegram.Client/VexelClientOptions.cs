using Telegram.Bot.Polling;

namespace Vexel.Telegram.Client;

/// <summary>
/// Holds configuration options for the <see cref="VexelClient"/>.
/// </summary>
public record VexelClientOptions
{
	/// <summary>
	/// Gets or sets the options for the update receiver.
	/// </summary>
	/// <remarks>
	/// Defaults to <see cref="ReceiverOptions"/> with <c>DropPendingUpdates</c> set to <c>true</c>.
	/// </remarks>
	public ReceiverOptions ReceiverOptions { get; set; } = new() { DropPendingUpdates = true, };
}
