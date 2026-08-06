using System.Diagnostics.CodeAnalysis;

namespace Vexel.Telegram.Handlers.Routing;

/// <summary>
/// Extracts the callback route key and optional suffix from <c>CallbackQuery.Data</c>
/// per the binding convention: key is everything up to the first <c>|</c>; suffix is
/// everything after (may itself contain <c>|</c>). Absent data yields no route.
/// </summary>
internal static class CallbackKeyExtractor
{
	/// <summary>
	/// Tries to split <paramref name="data"/> into route key and suffix.
	/// </summary>
	/// <param name="data">Raw <c>CallbackQuery.Data</c> (may be null).</param>
	/// <param name="key">Route key on success.</param>
	/// <param name="suffix">
	/// Suffix after the first <c>|</c>, or empty string when no separator is present.
	/// </param>
	/// <returns>
	/// <see langword="false"/> when <paramref name="data"/> is null (no developer data to route on).
	/// </returns>
	public static bool TryExtract(
		string? data,
		[NotNullWhen(true)] out string? key,
		[NotNullWhen(true)] out string? suffix)
	{
		if (data is null)
		{
			key = null;
			suffix = null;
			return false;
		}

		var separator = data.IndexOf('|', StringComparison.Ordinal);
		if (separator < 0)
		{
			key = data;
			suffix = string.Empty;
			return true;
		}

		key = data[..separator];
		suffix = data[(separator + 1)..];
		return true;
	}
}
