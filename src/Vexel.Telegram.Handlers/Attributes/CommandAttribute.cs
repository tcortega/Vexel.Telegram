namespace Vexel.Telegram.Handlers.Attributes;

/// <summary>
/// Marks an Immediate.Handlers handler as a Telegram bot command route.
/// Requires a sibling <c>[Handler]</c> attribute (Option A dual-attr model).
/// </summary>
/// <remarks>
/// Command names must match Telegram Bot API rules: 1-32 characters, lowercase English letters,
/// digits, and underscores only. The generator emits a route table entry and SetMyCommands metadata.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CommandAttribute : Attribute
{
	/// <summary>
	/// Initializes a new instance of the <see cref="CommandAttribute"/> class.
	/// </summary>
	/// <param name="name">
	/// Command name without a leading slash (e.g. <c>ping</c>). Compared case-insensitively at runtime.
	/// </param>
	public CommandAttribute(string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		Name = name;
	}

	/// <summary>Command name without a leading slash.</summary>
	public string Name { get; }

	/// <summary>
	/// Optional human-readable description used by SetMyCommands registration.
	/// </summary>
	public string? Description { get; set; }
}
