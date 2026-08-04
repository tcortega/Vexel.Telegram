namespace Vexel.Telegram.Handlers.Attributes;

/// <summary>
/// Marks an Immediate.Handlers handler as a Telegram callback-query route.
/// Requires a sibling <c>[Handler]</c> attribute (Option A dual-attr model).
/// </summary>
/// <remarks>
/// Route key is matched against <c>CallbackQuery.Data</c> up to the first <c>|</c>.
/// Everything after the first <c>|</c> is the optional suffix bound to a single
/// <see cref="string"/> request parameter (empty string when absent). The key itself
/// must not contain <c>|</c>. Total callback data must be at most 64 UTF-8 bytes
/// (Telegram Bot API limit; enforced by VEX0002 and keyboard helpers).
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CallbackAttribute : Attribute
{
	/// <summary>
	/// Initializes a new instance of the <see cref="CallbackAttribute"/> class.
	/// </summary>
	/// <param name="key">
	/// Callback route key without a <c>|</c> separator (e.g. <c>confirm</c>).
	/// Compared ordinally (case-sensitive) at runtime against the data prefix.
	/// </param>
	public CallbackAttribute(string key)
	{
		ArgumentNullException.ThrowIfNull(key);
		Key = key;
	}

	/// <summary>Callback route key (data prefix before the first <c>|</c>).</summary>
	public string Key { get; }
}
