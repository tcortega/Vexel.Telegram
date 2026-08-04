namespace Vexel.Telegram.Client;

/// <summary>
/// Supplies bot commands for automatic <c>setMyCommands</c> registration at host start.
/// Implemented by the composed router; absent when the app wires the client without routes.
/// </summary>
public interface IBotCommandCatalog
{
	/// <summary>Commands to register, already de-duplicated and in a stable order.</summary>
	IReadOnlyList<BotCommandDescriptor> Commands { get; }
}

/// <summary>
/// One bot command for <c>setMyCommands</c>.
/// </summary>
/// <param name="Name">Command name without a leading slash.</param>
/// <param name="Description">Human-readable description; may be empty before normalization.</param>
public sealed record BotCommandDescriptor(string Name, string Description);
