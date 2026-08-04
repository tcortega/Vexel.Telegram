using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Vexel.Telegram.Handlers.Routing;

/// <summary>
/// Shared helpers used by generated command binders. Keep parse logic here so emitted code stays thin
/// and the binding convention stays in one place. No reflection on the hot path.
/// </summary>
public static class CommandArgumentBinder
{
	/// <summary>
	/// Splits <paramref name="payload"/> on whitespace into at most <paramref name="tokenCount"/> tokens.
	/// When <paramref name="trailingTakesRest"/> is true, the final token receives the untokenized remainder
	/// after the preceding tokens (binding convention: trailing <see cref="string"/> param).
	/// </summary>
	/// <param name="payload">Trimmed argument text after the command entity.</param>
	/// <param name="tokenCount">Number of tokens to produce.</param>
	/// <param name="trailingTakesRest">
	/// When true, token N-1 is the remainder after tokens 0..N-2 (may be empty).
	/// When false, every token is a single whitespace-delimited word and leftover text is a failure.
	/// </param>
	/// <param name="tokens">Produced tokens on success.</param>
	/// <returns>False when the payload cannot supply the required tokens.</returns>
	public static bool TryTokenize(
		string payload,
		int tokenCount,
		bool trailingTakesRest,
		[NotNullWhen(true)] out string[]? tokens)
	{
		ArgumentNullException.ThrowIfNull(payload);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tokenCount);

		tokens = null;
		var span = payload.AsSpan().Trim();
		var result = new string[tokenCount];

		for (var i = 0; i < tokenCount; i++)
		{
			span = span.TrimStart();
			if (span.IsEmpty)
			{
				// Missing token. Trailing string may be empty; non-string missing tokens fail.
				if (trailingTakesRest && i == tokenCount - 1)
				{
					result[i] = string.Empty;
					tokens = result;
					return true;
				}

				return false;
			}

			if (trailingTakesRest && i == tokenCount - 1)
			{
				result[i] = span.ToString();
				tokens = result;
				return true;
			}

			var space = IndexOfWhitespace(span);
			if (space < 0)
			{
				result[i] = span.ToString();
				span = default;
			}
			else
			{
				result[i] = span[..space].ToString();
				span = span[(space + 1)..];
			}
		}

		// Non-trailing-rest: leftover non-whitespace is extra input → fail.
		if (!trailingTakesRest && !span.TrimStart().IsEmpty)
		{
			return false;
		}

		tokens = result;
		return true;
	}

	/// <summary>Parses a command argument as <see cref="int"/>.</summary>
	public static bool TryParseInt(string text, out int value) =>
		int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

	/// <summary>Parses a command argument as <see cref="long"/>.</summary>
	public static bool TryParseLong(string text, out long value) =>
		long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

	/// <summary>Parses a command argument as <see cref="decimal"/>.</summary>
	public static bool TryParseDecimal(string text, out decimal value) =>
		decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

	/// <summary>Parses a command argument as <see cref="bool"/> (<c>true</c>/<c>false</c>, case-insensitive).</summary>
	public static bool TryParseBool(string text, out bool value) =>
		bool.TryParse(text, out value);

	/// <summary>Parses a command argument as an enum of type <typeparamref name="TEnum"/>.</summary>
	public static bool TryParseEnum<TEnum>(string text, out TEnum value)
		where TEnum : struct, Enum =>
		Enum.TryParse(text, ignoreCase: true, out value) && Enum.IsDefined(value);

	private static int IndexOfWhitespace(ReadOnlySpan<char> span)
	{
		for (var i = 0; i < span.Length; i++)
		{
			if (char.IsWhiteSpace(span[i]))
			{
				return i;
			}
		}

		return -1;
	}
}
