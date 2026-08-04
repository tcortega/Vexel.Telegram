using System.Diagnostics.CodeAnalysis;

namespace Vexel.Telegram.Handlers.Routing;

/// <summary>
/// Extracts the first whitespace token and remainder from an inline query string for trigger
/// matching (binding convention rule 4 / D8). Never consults <c>InlineQuery.Id</c>.
/// </summary>
public static class InlineQueryKeyExtractor
{
	/// <summary>
	/// Splits <paramref name="query"/> into a leading trigger token and the trimmed remainder.
	/// </summary>
	/// <param name="query">Raw <c>InlineQuery.Query</c> (may be empty; null treated as empty).</param>
	/// <param name="trigger">
	/// First non-whitespace token, or empty when the query has no tokens.
	/// </param>
	/// <param name="remainder">
	/// Text after the first token, trimmed; empty when the query is only the token (or empty).
	/// </param>
	/// <returns>
	/// Always <see langword="true"/>. Present for symmetry with other extractors; inline queries
	/// always have a string query to inspect.
	/// </returns>
	public static bool TryExtract(
		string? query,
		[NotNullWhen(true)] out string? trigger,
		[NotNullWhen(true)] out string? remainder)
	{
		query ??= string.Empty;

		var length = query.Length;
		var i = 0;
		while (i < length && char.IsWhiteSpace(query[i]))
		{
			i++;
		}

		if (i >= length)
		{
			trigger = string.Empty;
			remainder = string.Empty;
			return true;
		}

		var start = i;
		while (i < length && !char.IsWhiteSpace(query[i]))
		{
			i++;
		}

		trigger = query[start..i];
		remainder = i < length ? query[i..].Trim() : string.Empty;
		return true;
	}
}
