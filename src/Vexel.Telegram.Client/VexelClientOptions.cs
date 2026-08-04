using Vexel.Telegram.Client.Webhook;

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

	/// <summary>
	/// How updates are received. Defaults to <see cref="TelegramReceiveMode.Polling"/>.
	/// Polling and webhook are mutually exclusive; invalid combinations fail fast at host start.
	/// </summary>
	public TelegramReceiveMode ReceiveMode { get; set; } = TelegramReceiveMode.Polling;

	/// <summary>
	/// Webhook settings. Required when <see cref="ReceiveMode"/> is
	/// <see cref="TelegramReceiveMode.Webhook"/>; must be <see langword="null"/> in polling mode.
	/// </summary>
	public WebhookOptions? Webhook { get; set; }

	/// <summary>
	/// When <see langword="true"/> (default), host start registers bot commands via
	/// <c>setMyCommands</c> from the generated route catalog. Set <see langword="false"/> to opt out.
	/// </summary>
	public bool RegisterBotCommands { get; set; } = true;

	/// <summary>
	/// Validates receive-mode configuration. Throws when polling and webhook are both configured
	/// (or neither is usable).
	/// </summary>
	/// <exception cref="InvalidOperationException">Configuration is not a valid exclusive mode.</exception>
	public void ValidateReceiveMode()
	{
		switch (ReceiveMode)
		{
			case TelegramReceiveMode.Polling:
				if (Webhook is not null)
				{
					throw new InvalidOperationException(
						"VexelClientOptions is configured for both polling and webhook. "
						+ "Set ReceiveMode to Webhook and configure Webhook, or leave ReceiveMode as "
						+ "Polling and set Webhook to null.");
				}

				return;

			case TelegramReceiveMode.Webhook:
				if (Webhook is null)
				{
					throw new InvalidOperationException(
						"ReceiveMode is Webhook but VexelClientOptions.Webhook is null. "
						+ "Configure Webhook.Url and Webhook.SecretToken, or use polling.");
				}

				if (Webhook.Url is null || !Webhook.Url.IsAbsoluteUri)
				{
					throw new InvalidOperationException(
						"Webhook.Url is required when ReceiveMode is Webhook and must be an absolute URI.");
				}

				if (string.IsNullOrWhiteSpace(Webhook.SecretToken))
				{
					throw new InvalidOperationException(
						"Webhook.SecretToken is required when ReceiveMode is Webhook. "
						+ "Telegram sends it as the X-Telegram-Bot-Api-Secret-Token header.");
				}

				if (!IsValidSecretToken(Webhook.SecretToken))
				{
					throw new InvalidOperationException(
						"Webhook.SecretToken must be 1-256 characters of A-Z, a-z, 0-9, underscore, or hyphen.");
				}

				if (string.IsNullOrWhiteSpace(Webhook.Path))
				{
					throw new InvalidOperationException(
						"Webhook.Path must be a non-empty relative path for the explicit endpoint map call.");
				}

				return;

			default:
				throw new InvalidOperationException(
					$"Unknown TelegramReceiveMode value '{ReceiveMode}'.");
		}
	}

	/// <summary>
	/// Bot API secret_token charset: 1-256 chars of <c>A-Z</c>, <c>a-z</c>, <c>0-9</c>, <c>_</c>, <c>-</c>.
	/// </summary>
	internal static bool IsValidSecretToken(string token)
	{
		if (token.Length is < 1 or > 256)
		{
			return false;
		}

		foreach (var ch in token)
		{
			if (ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-')
			{
				continue;
			}

			return false;
		}

		return true;
	}
}
