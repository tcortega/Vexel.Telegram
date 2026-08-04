using Telegram.Bot.Types;

namespace Vexel.Telegram.Handlers.Contexts;

/// <summary>
/// Contextual information for a <see cref="Update.ChosenInlineResult"/> update.
/// Chosen results are chatless unless an inline message id is present for later edits.
/// </summary>
/// <param name="ChosenInlineResult">The inbound chosen inline result.</param>
public sealed record ChosenInlineResultContext(ChosenInlineResult ChosenInlineResult)
{
	/// <summary>Result id the user picked (developer-provided when answering the query).</summary>
	public string ResultId => ChosenInlineResult.ResultId;

	/// <summary>User who chose the result.</summary>
	public User From => ChosenInlineResult.From;

	/// <summary>User id of the chooser (lane key for chosen inline results).</summary>
	public long UserId => ChosenInlineResult.From.Id;

	/// <summary>Query string that produced the chosen result.</summary>
	public string? Query => ChosenInlineResult.Query;

	/// <summary>Inline message id when the chosen result sent a message the bot can edit.</summary>
	public string? InlineMessageId => ChosenInlineResult.InlineMessageId;
}
