using System.Text;

namespace Vexel.Telegram.Handlers.Keyboards;

/// <summary>
/// Formats and validates short Telegram callback_data values for Vexel routes
/// (<c>key</c> or <c>key|suffix</c>).
/// </summary>
public static class CallbackData
{
	/// <summary>Telegram Bot API maximum UTF-8 byte length for <c>callback_data</c>.</summary>
	public const int MaxUtf8ByteLength = 64;

	/// <summary>Separator between route key and optional suffix.</summary>
	public const char Separator = '|';

	/// <summary>
	/// Builds callback data as <paramref name="routeKey"/> or
	/// <c>{routeKey}|{suffix}</c>. Throws when the key contains
	/// <see cref="Separator"/> or the result exceeds <see cref="MaxUtf8ByteLength"/> UTF-8 bytes.
	/// </summary>
	/// <param name="routeKey">Route key matching a <c>[Callback]</c> attribute.</param>
	/// <param name="suffix">Optional suffix bound to the handler's string parameter.</param>
	/// <returns>The callback_data string to put on an inline keyboard button.</returns>
	public static string Format(string routeKey, string? suffix = null)
	{
		ArgumentNullException.ThrowIfNull(routeKey);

		if (routeKey.Contains(Separator.ToString(), StringComparison.Ordinal))
		{
			throw new ArgumentException(
				$"Callback route key must not contain '{Separator}'.",
				nameof(routeKey));
		}

		var data = suffix is null ? routeKey : string.Concat(routeKey, Separator, suffix);
		EnsureWithinLimit(data);
		return data;
	}

	/// <summary>
	/// Throws <see cref="ArgumentException"/> when <paramref name="data"/> exceeds
	/// <see cref="MaxUtf8ByteLength"/> UTF-8 bytes.
	/// </summary>
	/// <param name="data">Candidate callback_data.</param>
	public static void EnsureWithinLimit(string data)
	{
		ArgumentNullException.ThrowIfNull(data);

		var byteCount = Encoding.UTF8.GetByteCount(data);
		if (byteCount > MaxUtf8ByteLength)
		{
			throw new ArgumentException(
				$"Callback data is {byteCount} UTF-8 bytes; Telegram allows at most {MaxUtf8ByteLength}.",
				nameof(data));
		}
	}

	/// <summary>
	/// Returns whether <paramref name="data"/> is within the Telegram 64-byte UTF-8 limit.
	/// </summary>
	/// <param name="data">Candidate callback_data.</param>
	/// <returns><see langword="true"/> when the UTF-8 byte length is at most 64.</returns>
	public static bool IsWithinLimit(string data)
	{
		ArgumentNullException.ThrowIfNull(data);
		return Encoding.UTF8.GetByteCount(data) <= MaxUtf8ByteLength;
	}
}
