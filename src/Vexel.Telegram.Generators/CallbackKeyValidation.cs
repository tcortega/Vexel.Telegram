using System.Text;

namespace Vexel.Telegram.Generators;

/// <summary>
/// Route-key kinds that share the callback key rules (non-blank, no <c>|</c>, 64 UTF-8 bytes).
/// </summary>
internal enum RouteKeyKind
{
	/// <summary><c>[Callback]</c> keys, carried in <c>callback_data</c>.</summary>
	Callback,

	/// <summary><c>[ChosenInlineResult]</c> keys, carried in the inline result <c>ResultId</c>.</summary>
	ChosenInlineResult,
}

/// <summary>
/// Shared validation for <c>[Callback]</c> and <c>[ChosenInlineResult]</c> route keys
/// (VEX0002 + generator emission gate).
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
	/// <param name="kind">Route kind the key belongs to, used to name the Telegram payload field.</param>
	public static string DescribeFailure(string key, RouteKeyKind kind)
	{
		var keyLabel = kind == RouteKeyKind.Callback ? "Callback route key" : "Chosen inline result route key";
		var payloadLabel = kind == RouteKeyKind.Callback ? "callback_data" : "ResultId";

		if (string.IsNullOrWhiteSpace(key))
		{
			return $"{keyLabel} must not be empty or whitespace; Telegram requires 1-{MaxUtf8ByteLength} UTF-8 bytes of {payloadLabel}";
		}

		if (key.IndexOf('|') >= 0)
		{
			return $"{keyLabel} '{key}' must not contain '|' (the key/suffix separator)";
		}

		var byteCount = Encoding.UTF8.GetByteCount(key);
		return $"{keyLabel} is {byteCount} UTF-8 bytes; Telegram allows at most {MaxUtf8ByteLength} for {payloadLabel}";
	}
}
