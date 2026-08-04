namespace Vexel.Telegram.Handlers.Routing;

/// <summary>
/// One assembly's contribution to the composed Telegram route table.
/// Generated <c>Add{Assembly}Telegram()</c> methods register one of these as a singleton;
/// <see cref="TelegramRouter"/> merges them at host start and fails fast on cross-assembly
/// duplicate keys.
/// </summary>
public sealed class TelegramRouteContribution
{
	/// <summary>
	/// Initializes a new contribution.
	/// </summary>
	/// <param name="assemblyName">
	/// Display name of the contributing assembly (used in duplicate-key error messages).
	/// </param>
	/// <param name="commands">
	/// Command-key (case-insensitive) to binder map. Must already use an ordinal-ignore-case comparer.
	/// </param>
	/// <param name="commandMetadata">
	/// SetMyCommands metadata in a stable order (typically sorted by command name).
	/// </param>
	public TelegramRouteContribution(
		string assemblyName,
		IReadOnlyDictionary<string, RouteBinder> commands,
		IReadOnlyList<CommandRouteMetadata> commandMetadata)
	{
		ArgumentNullException.ThrowIfNull(assemblyName);
		ArgumentNullException.ThrowIfNull(commands);
		ArgumentNullException.ThrowIfNull(commandMetadata);

		AssemblyName = assemblyName;
		Commands = commands;
		CommandMetadata = commandMetadata;
	}

	/// <summary>Display name of the contributing assembly.</summary>
	public string AssemblyName { get; }

	/// <summary>Command route map for this assembly.</summary>
	public IReadOnlyDictionary<string, RouteBinder> Commands { get; }

	/// <summary>SetMyCommands metadata for this assembly.</summary>
	public IReadOnlyList<CommandRouteMetadata> CommandMetadata { get; }
}
