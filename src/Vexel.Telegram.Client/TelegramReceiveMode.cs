namespace Vexel.Telegram.Client;

/// <summary>
/// How <see cref="VexelClient"/> receives updates from Telegram.
/// Polling and webhook are mutually exclusive.
/// </summary>
public enum TelegramReceiveMode
{
	/// <summary>Long-poll via <c>getUpdates</c>. Default.</summary>
	Polling = 0,

	/// <summary>
	/// Outgoing webhook. Requires <see cref="VexelClientOptions.Webhook"/> and an explicit
	/// endpoint registration (e.g. <c>MapTelegramWebhook</c>); nothing is auto-mapped.
	/// </summary>
	Webhook = 1,
}
