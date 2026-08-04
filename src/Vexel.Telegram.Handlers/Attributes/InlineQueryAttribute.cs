namespace Vexel.Telegram.Handlers.Attributes;

/// <summary>
/// Marks an Immediate.Handlers handler as a Telegram inline-query route.
/// Requires a sibling <c>[Handler]</c> attribute (Option A dual-attr model).
/// </summary>
/// <remarks>
/// Routing keys off the query text, never <c>InlineQuery.Id</c> (Telegram's opaque server id).
/// The first whitespace token of the query, matched exactly (ordinal), selects a trigger handler
/// when that token is a registered non-empty trigger; the remainder after the token, trimmed,
/// binds as a single <see cref="string"/> request parameter (empty string when absent).
/// Empty or unmatched queries route to the empty-trigger default handler with the full query text.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class InlineQueryAttribute : Attribute
{
	/// <summary>
	/// Initializes a new instance of the <see cref="InlineQueryAttribute"/> class.
	/// </summary>
	/// <param name="trigger">
	/// Trigger token matched against the first whitespace token of the query (e.g. <c>search</c>).
	/// Pass <see cref="string.Empty"/> (the default) to register the default handler for empty and
	/// unmatched queries. Trigger tokens must not contain whitespace.
	/// </param>
	public InlineQueryAttribute(string trigger = "")
	{
		ArgumentNullException.ThrowIfNull(trigger);
		Trigger = trigger;
	}

	/// <summary>
	/// Inline query trigger token, or empty string for the default handler.
	/// </summary>
	public string Trigger { get; }
}
