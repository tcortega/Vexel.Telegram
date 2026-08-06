using Vexel.Telegram.Client;

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
	/// Flow step-key to binder map. The key is the <see cref="Type.FullName"/> of the handler's
	/// <em>request</em> type, not of the handler class - a nested request renders as
	/// <c>Demo.CollectName+Command</c>. Must already use an ordinal comparer.
	/// </param>
	/// <param name="onMessages">
	/// <c>[OnMessage]</c> observers for this assembly, already sorted by handler fully-qualified
	/// metadata name.
	/// </param>
	/// <param name="onCallbackQueries">
	/// <c>[OnCallbackQuery]</c> observers for this assembly, already sorted by handler FQ name.
	/// </param>
	/// <param name="onInlineQueries">
	/// <c>[OnInlineQuery]</c> observers for this assembly, already sorted by handler FQ name.
	/// </param>
	/// <param name="onChosenInlineResults">
	/// <c>[OnChosenInlineResult]</c> observers for this assembly, already sorted by handler FQ name.
	/// </param>
	public TelegramRouteContribution(
		string assemblyName,
		IReadOnlyDictionary<string, RouteBinder> commands,
		IReadOnlyList<BotCommandDescriptor> commandMetadata,
		IReadOnlyDictionary<string, RouteBinder>? callbacks = null,
		IReadOnlyDictionary<string, RouteBinder>? inlineQueries = null,
		IReadOnlyDictionary<string, RouteBinder>? chosenInlineResults = null,
		IReadOnlyDictionary<string, RouteBinder>? flowSteps = null,
		IReadOnlyList<OnHandlerEntry>? onMessages = null,
		IReadOnlyList<OnHandlerEntry>? onCallbackQueries = null,
		IReadOnlyList<OnHandlerEntry>? onInlineQueries = null,
		IReadOnlyList<OnHandlerEntry>? onChosenInlineResults = null)
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
		OnMessages = onMessages ?? [];
		OnCallbackQueries = onCallbackQueries ?? [];
		OnInlineQueries = onInlineQueries ?? [];
		OnChosenInlineResults = onChosenInlineResults ?? [];
	}

	/// <summary>Display name of the contributing assembly.</summary>
	public string AssemblyName { get; }

	/// <summary>Command route map for this assembly.</summary>
	public IReadOnlyDictionary<string, RouteBinder> Commands { get; }

	/// <summary>SetMyCommands metadata for this assembly.</summary>
	public IReadOnlyList<BotCommandDescriptor> CommandMetadata { get; }

	/// <summary>Callback route map for this assembly.</summary>
	public IReadOnlyDictionary<string, RouteBinder> Callbacks { get; }

	/// <summary>Inline-query route map for this assembly (empty key = default handler).</summary>
	public IReadOnlyDictionary<string, RouteBinder> InlineQueries { get; }

	/// <summary>Chosen-inline-result route map for this assembly.</summary>
	public IReadOnlyDictionary<string, RouteBinder> ChosenInlineResults { get; }

	/// <summary>Flow step map for this assembly (request <see cref="Type.FullName"/> → binder).</summary>
	public IReadOnlyDictionary<string, RouteBinder> FlowSteps { get; }

	/// <summary><c>[OnMessage]</c> observers for this assembly.</summary>
	public IReadOnlyList<OnHandlerEntry> OnMessages { get; }

	/// <summary><c>[OnCallbackQuery]</c> observers for this assembly.</summary>
	public IReadOnlyList<OnHandlerEntry> OnCallbackQueries { get; }

	/// <summary><c>[OnInlineQuery]</c> observers for this assembly.</summary>
	public IReadOnlyList<OnHandlerEntry> OnInlineQueries { get; }

	/// <summary><c>[OnChosenInlineResult]</c> observers for this assembly.</summary>
	public IReadOnlyList<OnHandlerEntry> OnChosenInlineResults { get; }
}
