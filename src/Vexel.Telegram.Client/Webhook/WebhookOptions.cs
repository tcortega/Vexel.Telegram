namespace Vexel.Telegram.Client.Webhook;

/// <summary>
/// Webhook receive settings. Required when <see cref="VexelClientOptions.ReceiveMode"/> is
/// <see cref="TelegramReceiveMode.Webhook"/>.
/// </summary>
public sealed class WebhookOptions
{
	/// <summary>
	/// Default relative path used by the explicit ASP.NET Core endpoint mapper.
	/// </summary>
	public const string DefaultPath = "/telegram/webhook";

	/// <summary>
	/// HTTP header Telegram sends when <see cref="SecretToken"/> was set on <c>setWebhook</c>.
	/// </summary>
	public const string SecretTokenHeaderName = "X-Telegram-Bot-Api-Secret-Token";

	/// <summary>
	/// Public HTTPS URL passed to Telegram <c>setWebhook</c>.
	/// </summary>
	public Uri? Url { get; set; }

	/// <summary>
	/// Secret token set on <c>setWebhook</c> and required on every inbound request
	/// (header <see cref="SecretTokenHeaderName"/>). 1-256 chars of <c>A-Z</c>, <c>a-z</c>,
	/// <c>0-9</c>, <c>_</c>, <c>-</c> per the Bot API.
	/// </summary>
	public string SecretToken { get; set; } = string.Empty;

	/// <summary>
	/// Relative path the app must map explicitly (no auto-mapped route). Defaults to
	/// <see cref="DefaultPath"/>.
	/// </summary>
	public string Path { get; set; } = DefaultPath;
}
