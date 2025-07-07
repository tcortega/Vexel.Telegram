using System.Diagnostics.CodeAnalysis;

namespace Vexel.Telegram.Interactivity;

/// <summary>
/// Provides helper methods for working with interaction IDs.
/// </summary>
[SuppressMessage("Usage", "MA0015:Specify the parameter name in ArgumentException")]
public static class InteractionIdHelper
{
	/// <summary>
	/// Creates a callback button ID with optional state data.
	/// </summary>
	/// <param name="id">The button ID.</param>
	/// <param name="state">Optional state data.</param>
	/// <param name="path">
	/// The group path to the component; that is, the outer groups that must be traversed before reaching the group
	/// where the component's handler is declared.
	/// </param>
	/// <returns>The formatted callback data.</returns>
	public static string CreateCallbackButtonId(string id, string? state = null, params string[] path)
		=> FormatId(Constants.CallbackButtonPrefix, id, state, path);

	/// <summary>
	/// Creates a chosen inline result ID with optional state data.
	/// </summary>
	/// <param name="id">The result ID.</param>
	/// <param name="state">Optional state data.</param>
	/// <param name="path">
	/// The group path to the component; that is, the outer groups that must be traversed before reaching the group
	/// where the component's handler is declared.
	/// </param>
	/// <returns>The formatted result ID.</returns>
	public static string CreateChosenInlineResultId(string id, string? state = null, params string[] path)
		=> FormatId(Constants.ChosenInlineResultPrefix, id, state, path);

	/// <summary>
	/// Creates an inline query ID with optional state data.
	/// </summary>
	/// <param name="id">The query ID.</param>
	/// <param name="state">Optional state data.</param>
	/// <param name="path">
	/// The group path to the component; that is, the outer groups that must be traversed before reaching the group
	/// where the component's handler is declared.
	/// </param>
	/// <returns>The formatted query ID.</returns>
	public static string CreateInlineQueryId(string id, string? state = null, params string[] path)
		=> FormatId(Constants.InlineQueryPrefix, id, state, path);

	/// <summary>
	/// Creates the ID for a future text message.
	/// </summary>
	/// <param name="id">The ID of the handler.</param>
	/// <param name="state">Optional state data.</param>
	/// <param name="path">
	/// The group path to the component; that is, the outer groups that must be traversed before reaching the group
	/// where the component's handler is declared.
	/// </param>
	/// <returns>The formatted message ID.</returns>
	public static string CreateTextResponseId(string id, string? state = null, params string[] path)
		=> FormatId(Constants.TextResponsePrefix, id, state, path);

	private static string FormatId(string type, string name, string? state, string[] path)
	{
		ValidateParameters(type, name, state, path);

		var combinedPath = path.Length > 0 ? $"{string.Join(' ', path)} " : string.Empty;
		var statePrefix = state != null ? $"{Constants.StatePrefix}{state} " : string.Empty;
		return $"{Constants.InteractionTree}::{statePrefix}{combinedPath}{type}::{name}";
	}

	private static void ValidateParameters(string type, string name, string? state, string[] path)
	{
		var parameters = new[] { type, name }.Concat(path);
		if (state != null)
		{
			parameters = parameters.Concat([state]);
		}

		foreach (var parameter in parameters)
		{
			if (string.IsNullOrWhiteSpace(parameter))
			{
				throw new ArgumentException("Parameters must consist of some non-whitespace characters.");
			}

			if (parameter.Any(char.IsWhiteSpace))
			{
				throw new ArgumentException("Parameters may not contain whitespace.");
			}
		}
	}

	internal static bool IsFromInteractionTree(string id)
	{
		return id.StartsWith($"{Constants.InteractionTree}::", StringComparison.Ordinal);
	}
}
