using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Vexel.Telegram.Client.Dispatch;

namespace Vexel.Telegram.Client.Webhook;

/// <summary>
/// Framework-agnostic webhook ingress: verifies <c>secret_token</c>, deserializes the update,
/// and schedules it on the same per-chat lanes polling uses.
/// </summary>
public sealed class WebhookUpdateReceiver(
	UpdateScheduler scheduler,
	IOptions<VexelClientOptions> options,
	ILogger<WebhookUpdateReceiver> logger)
{
	private readonly VexelClientOptions _options = options.Value;

	/// <summary>
	/// Processes one inbound webhook body.
	/// </summary>
	/// <param name="body">Request body stream (JSON <see cref="Update"/>).</param>
	/// <param name="secretTokenHeader">
	/// Value of the <see cref="WebhookOptions.SecretTokenHeaderName"/> header, or <see langword="null"/>.
	/// </param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>HTTP status the endpoint should return to Telegram.</returns>
	public async Task<HttpStatusCode> ProcessAsync(
		Stream body,
		string? secretTokenHeader,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(body);

		var webhook = _options.Webhook
			?? throw new InvalidOperationException(
				"WebhookUpdateReceiver requires VexelClientOptions.Webhook to be configured.");

		if (!SecretTokensEqual(secretTokenHeader, webhook.SecretToken))
		{
			logger.LogWarning("Rejected webhook update: secret_token header missing or invalid.");
			return HttpStatusCode.Forbidden;
		}

		Update? update;
		try
		{
			update = await JsonSerializer
				.DeserializeAsync<Update>(body, JsonBotAPI.Options, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (JsonException ex)
		{
			logger.LogWarning(ex, "Rejected webhook update: body is not a valid Update JSON payload.");
			return HttpStatusCode.BadRequest;
		}

		if (update is null)
		{
			logger.LogWarning("Rejected webhook update: body deserialized to null.");
			return HttpStatusCode.BadRequest;
		}

		try
		{
			await scheduler.ScheduleAsync(update, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			// Never fail the HTTP response for a schedule fault - Telegram would retry aggressively.
			logger.LogError(ex, "Failed to schedule webhook update {UpdateId}", update.Id);
		}

		return HttpStatusCode.OK;
	}

	/// <summary>
	/// Constant-time comparison of the inbound secret header against the configured token.
	/// </summary>
	internal static bool SecretTokensEqual(string? provided, string expected)
	{
		if (string.IsNullOrEmpty(provided) || string.IsNullOrEmpty(expected))
		{
			return false;
		}

		var providedBytes = Encoding.UTF8.GetBytes(provided);
		var expectedBytes = Encoding.UTF8.GetBytes(expected);

		if (providedBytes.Length != expectedBytes.Length)
		{
			return false;
		}

		return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
	}
}
