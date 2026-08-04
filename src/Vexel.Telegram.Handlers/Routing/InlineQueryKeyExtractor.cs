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
	/// <returns>
	/// The first non-whitespace token (empty when the query has no tokens) and the text after that
	/// token, trimmed (empty when the query is only the token).
	/// </returns>
	public static (string Trigger, string Remainder) Extract(string? query)
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
			return (string.Empty, string.Empty);
		}

		var start = i;
		while (i < length && !char.IsWhiteSpace(query[i]))
		{
			i++;
		}

		var trigger = query[start..i];
		var remainder = i < length ? query[i..].Trim() : string.Empty;
		return (trigger, remainder);
	}
}
