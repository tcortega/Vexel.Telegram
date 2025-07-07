namespace Vexel.Telegram.Commands;

/// <summary>
/// Represents a set of options relevant to a command responder.
/// </summary>
public class CommandResponderOptions
{
	/// <summary>
	/// Gets or sets the prefix that commands must start with.
	/// </summary>
	public string? Prefix { get; set; } = "/";
}
