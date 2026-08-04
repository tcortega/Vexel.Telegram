using System.Text;

namespace Vexel.Telegram.Generators;

/// <summary>
/// Shared validation for <c>[Callback]</c> route keys (VEX0002 + generator emission gate).
/// </summary>
internal static class CallbackKeyValidation
{
	/// <summary>Telegram Bot API maximum UTF-8 byte length for callback_data.</summary>
	public const int MaxUtf8ByteLength = 64;

	/// <summary>
	/// Returns whether <paramref name="key"/> is a valid callback route key: non-blank, no <c>|</c>
	/// separator, and UTF-8 byte length at most <see cref="MaxUtf8ByteLength"/>.
	/// </summary>
	/// <param name="key">Candidate route key from <c>[Callback]</c>.</param>
	public static bool IsValid(string key)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			return false;
		}

		if (key.IndexOf('|') >= 0)
		{
			return false;
		}

		return Encoding.UTF8.GetByteCount(key) <= MaxUtf8ByteLength;
	}

	/// <summary>
	/// Describes why <paramref name="key"/> failed <see cref="IsValid"/>.
	/// </summary>
	/// <param name="key">Invalid key.</param>
	public static string DescribeFailure(string key)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			return $"Callback route key must not be empty or whitespace; Telegram requires 1-{MaxUtf8ByteLength} UTF-8 bytes of callback_data";
		}

		if (key.IndexOf('|') >= 0)
		{
			return $"Callback route key '{key}' must not contain '|' (the key/suffix separator)";
		}

		var byteCount = Encoding.UTF8.GetByteCount(key);
		return $"Callback route key is {byteCount} UTF-8 bytes; Telegram allows at most {MaxUtf8ByteLength} for callback_data";
	}
}
