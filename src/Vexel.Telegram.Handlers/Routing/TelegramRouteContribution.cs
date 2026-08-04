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
	/// <param name="callbacks">
	/// Callback-key (ordinal, case-sensitive) to binder map. Must already use an ordinal comparer.
	/// </param>
	/// <param name="inlineQueries">
	/// Inline-query trigger (ordinal, case-sensitive; empty string = default) to binder map.
	/// Must already use an ordinal comparer.
	/// </param>
	/// <param name="chosenInlineResults">
	/// Chosen-inline-result key (ordinal, case-sensitive ResultId prefix) to binder map.
	/// Must already use an ordinal comparer.
	/// </param>
	/// <param name="flowSteps">
	/// Flow step-key (<see cref="Type.FullName"/> of the handler type, ordinal) to binder map.
	/// Must already use an ordinal comparer.
	/// </param>
	public TelegramRouteContribution(
		string assemblyName,
		IReadOnlyDictionary<string, RouteBinder> commands,
		IReadOnlyList<CommandRouteMetadata> commandMetadata,
		IReadOnlyDictionary<string, RouteBinder>? callbacks = null,
		IReadOnlyDictionary<string, RouteBinder>? inlineQueries = null,
		IReadOnlyDictionary<string, RouteBinder>? chosenInlineResults = null,
		IReadOnlyDictionary<string, RouteBinder>? flowSteps = null)
	{
		ArgumentNullException.ThrowIfNull(assemblyName);
		ArgumentNullException.ThrowIfNull(commands);
		ArgumentNullException.ThrowIfNull(commandMetadata);

		AssemblyName = assemblyName;
		Commands = commands;
		CommandMetadata = commandMetadata;
		Callbacks = callbacks ?? new Dictionary<string, RouteBinder>(StringComparer.Ordinal);
		InlineQueries = inlineQueries ?? new Dictionary<string, RouteBinder>(StringComparer.Ordinal);
		ChosenInlineResults = chosenInlineResults ?? new Dictionary<string, RouteBinder>(StringComparer.Ordinal);
		FlowSteps = flowSteps ?? new Dictionary<string, RouteBinder>(StringComparer.Ordinal);
	}

	/// <summary>Display name of the contributing assembly.</summary>
	public string AssemblyName { get; }

	/// <summary>Command route map for this assembly.</summary>
	public IReadOnlyDictionary<string, RouteBinder> Commands { get; }

	/// <summary>SetMyCommands metadata for this assembly.</summary>
	public IReadOnlyList<CommandRouteMetadata> CommandMetadata { get; }

	/// <summary>Callback route map for this assembly.</summary>
	public IReadOnlyDictionary<string, RouteBinder> Callbacks { get; }

	/// <summary>Inline-query route map for this assembly (empty key = default handler).</summary>
	public IReadOnlyDictionary<string, RouteBinder> InlineQueries { get; }

	/// <summary>Chosen-inline-result route map for this assembly.</summary>
	public IReadOnlyDictionary<string, RouteBinder> ChosenInlineResults { get; }

	/// <summary>Flow step map for this assembly (handler <see cref="Type.FullName"/> → binder).</summary>
	public IReadOnlyDictionary<string, RouteBinder> FlowSteps { get; }
}
