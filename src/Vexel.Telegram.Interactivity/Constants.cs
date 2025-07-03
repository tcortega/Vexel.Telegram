namespace Vexel.Telegram.Interactivity;

/// <summary>
/// Defines various interaction-related constants.
/// </summary>
public static class Constants
{
	/// <summary>
	/// Gets the name of the interaction command tree.
	/// </summary>
	public const string InteractionTree = "vexel::interactivity";

	/// <summary>
	/// Gets the prefix used for passing the state.
	/// </summary>
	public const string StatePrefix = "$!";

	/// <summary>
	/// Gets the prefix used for callback buttons.
	/// </summary>
	public const string CallbackButtonPrefix = "button";

	/// <summary>
	/// Gets the prefix used for chosen inline results.
	/// </summary>
	public const string ChosenInlineResultPrefix = "chosen";

	/// <summary>
	/// Gets the prefix used for inline queries.
	/// </summary>
	public const string InlineQueryPrefix = "inline";
}
