using Telegram.Bot.Types;

namespace Vexel.Telegram.Client;

/// <summary>
/// Builds the <c>setMyCommands</c> payload from catalog descriptors.
/// </summary>
public static class BotCommandRegistration
{
	/// <summary>
	/// Converts catalog descriptors into Telegram <see cref="BotCommand"/> values.
	/// Empty descriptions fall back to the command name so the API's non-empty description
	/// rule is always satisfied (names are already 1-32 chars by generator validation).
	/// </summary>
	/// <param name="commands">Descriptors from <see cref="IBotCommandCatalog"/>.</param>
	/// <returns>Payload suitable for <c>setMyCommands</c>, ordered as given.</returns>
	public static IReadOnlyList<BotCommand> BuildPayload(IEnumerable<BotCommandDescriptor> commands)
	{
		ArgumentNullException.ThrowIfNull(commands);

		var payload = new List<BotCommand>();
		foreach (var command in commands)
		{
			ArgumentNullException.ThrowIfNull(command);
			ArgumentException.ThrowIfNullOrWhiteSpace(command.Name);

			var description = string.IsNullOrWhiteSpace(command.Description)
				? command.Name
				: command.Description.Trim();

			payload.Add(new BotCommand
			{
				Command = command.Name,
				Description = description,
			});
		}

		return payload;
	}
}
