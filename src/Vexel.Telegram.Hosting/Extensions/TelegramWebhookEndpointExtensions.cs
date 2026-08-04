using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vexel.Telegram.Client;
using Vexel.Telegram.Client.Webhook;

namespace Vexel.Telegram.Hosting.Extensions;

/// <summary>
/// Explicit ASP.NET Core endpoint registration for Telegram webhook ingress.
/// Nothing is auto-mapped; the app must call <see cref="MapTelegramWebhook"/>.
/// </summary>
public static class TelegramWebhookEndpointExtensions
{
	/// <summary>
	/// Maps a POST endpoint that verifies the webhook <c>secret_token</c> header and schedules
	/// the update. Path defaults to <see cref="WebhookOptions.Path"/> from options.
	/// </summary>
	/// <param name="endpoints">The endpoint route builder.</param>
	/// <param name="pattern">
	/// Optional path override. When <see langword="null"/>, uses the configured webhook path
	/// (or <see cref="WebhookOptions.DefaultPath"/> when webhook options are absent).
	/// </param>
	/// <returns>The endpoint convention builder.</returns>
	public static IEndpointConventionBuilder MapTelegramWebhook(
		this IEndpointRouteBuilder endpoints,
		[StringSyntax("Route")] string? pattern = null)
	{
		ArgumentNullException.ThrowIfNull(endpoints);

		var path = pattern;
		if (string.IsNullOrWhiteSpace(path))
		{
			var options = endpoints.ServiceProvider
				.GetRequiredService<IOptions<VexelClientOptions>>()
				.Value;
			path = options.Webhook?.Path ?? WebhookOptions.DefaultPath;
		}

		return endpoints.MapPost(path, static async (
			HttpContext httpContext,
			WebhookUpdateReceiver receiver) =>
		{
			var header = httpContext.Request.Headers[WebhookOptions.SecretTokenHeaderName].ToString();
			// Empty header string from ToString() is treated as missing.
			var secret = string.IsNullOrEmpty(header) ? null : header;

			var status = await receiver
				.ProcessAsync(httpContext.Request.Body, secret, httpContext.RequestAborted)
				.ConfigureAwait(false);

			httpContext.Response.StatusCode = (int)status;
		});
	}
}
