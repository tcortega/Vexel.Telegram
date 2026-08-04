using Telegram.Bot.Types;

namespace Vexel.Telegram.Handlers.Contexts;

/// <summary>
/// Contextual information for an <see cref="Update.InlineQuery"/> update.
/// Inline queries are chatless; identify the user via <see cref="UserId"/>.
/// </summary>
/// <param name="InlineQuery">The inbound inline query.</param>
public sealed record InlineQueryContext(InlineQuery InlineQuery)
{
	/// <summary>Opaque inline query id used with <c>answerInlineQuery</c>.</summary>
	public string Id => InlineQuery.Id;

	/// <summary>User who typed the inline query.</summary>
	public User From => InlineQuery.From;

	/// <summary>User id of the querier (lane key for inline queries).</summary>
	public long UserId => InlineQuery.From.Id;

	/// <summary>Text the user typed after the bot username.</summary>
	public string Query => InlineQuery.Query;

	/// <summary>Offset of results requested by the client, if any.</summary>
	public string? Offset => InlineQuery.Offset;
}
