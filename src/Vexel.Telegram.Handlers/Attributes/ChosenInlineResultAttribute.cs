namespace Vexel.Telegram.Handlers.Attributes;

/// <summary>
/// Marks an Immediate.Handlers handler as a Telegram chosen-inline-result route.
/// Requires a sibling <c>[Handler]</c> attribute (Option A dual-attr model).
/// </summary>
/// <remarks>
/// Route key is matched against the developer-set <c>ChosenInlineResult.ResultId</c> up to the
/// first <c>|</c> (same key/suffix convention as callbacks). Everything after the first <c>|</c>
/// binds to a single <see cref="string"/> request parameter (empty string when absent). The key
/// itself must not contain <c>|</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ChosenInlineResultAttribute : Attribute
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ChosenInlineResultAttribute"/> class.
	/// </summary>
	/// <param name="key">
	/// ResultId route key without a <c>|</c> separator (e.g. <c>item</c>).
	/// Compared ordinally (case-sensitive) at runtime against the ResultId prefix.
	/// </param>
	public ChosenInlineResultAttribute(string key)
	{
		ArgumentNullException.ThrowIfNull(key);
		Key = key;
	}

	/// <summary>Chosen-inline-result route key (ResultId prefix before the first <c>|</c>).</summary>
	public string Key { get; }
}
