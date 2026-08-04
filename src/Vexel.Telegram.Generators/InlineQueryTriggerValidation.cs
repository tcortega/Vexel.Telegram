namespace Vexel.Telegram.Generators;

/// <summary>
/// Shared validation for <c>[InlineQuery]</c> trigger tokens (generator emission gate).
/// </summary>
internal static class InlineQueryTriggerValidation
{
	/// <summary>
	/// Returns whether <paramref name="trigger"/> is a valid inline-query trigger: empty (default
	/// handler) or a non-whitespace token with no embedded whitespace.
	/// </summary>
	/// <param name="trigger">Candidate trigger from <c>[InlineQuery]</c>.</param>
	public static bool IsValid(string trigger)
	{
		if (trigger.Length == 0)
		{
			return true;
		}

		if (string.IsNullOrWhiteSpace(trigger))
		{
			return false;
		}

		foreach (var c in trigger)
		{
			if (char.IsWhiteSpace(c))
			{
				return false;
			}
		}

		return true;
	}
}
