namespace Vexel.Telegram.Generators;

internal sealed record CommandRouteModel(
	string CommandName,
	string? Description,
	string HandlerFullyQualifiedName,
	string RequestFullyQualifiedName,
	string AssemblyDisplayName,
	EquatableReadOnlyList<RequestParameterModel> Parameters);

internal sealed record CallbackRouteModel(
	string RouteKey,
	string HandlerFullyQualifiedName,
	string RequestFullyQualifiedName,
	string AssemblyDisplayName,
	bool HasStringParameter);

/// <summary>
/// Inline-query route. <see cref="Trigger"/> is empty for the default handler.
/// </summary>
internal sealed record InlineQueryRouteModel(
	string Trigger,
	string HandlerFullyQualifiedName,
	string RequestFullyQualifiedName,
	string AssemblyDisplayName,
	bool HasStringParameter);

internal sealed record ChosenInlineResultRouteModel(
	string RouteKey,
	string HandlerFullyQualifiedName,
	string RequestFullyQualifiedName,
	string AssemblyDisplayName,
	bool HasStringParameter);

internal sealed record FlowStepModel(
	string StepKey,
	string HandlerFullyQualifiedName,
	string RequestFullyQualifiedName,
	string AssemblyDisplayName,
	bool HasStringParameter);

/// <summary>
/// On* observer. Request is always an empty record; payload access is via injected context.
/// Sorted by <see cref="HandlerFullyQualifiedName"/> for deterministic dispatch.
/// </summary>
internal sealed record OnHandlerModel(
	string HandlerFullyQualifiedName,
	string RequestFullyQualifiedName,
	string AssemblyDisplayName);

internal sealed record RequestParameterModel(
	string FullyQualifiedTypeName,
	string ParameterName,
	BindableParameterKind Kind,
	bool IsTrailingString);

internal enum BindableParameterKind
{
	String,
	Int,
	Long,
	Bool,
	Decimal,
	Enum,
}

internal sealed record AssemblyRoutesModel(
	string AssemblyIdentifier,
	string RootNamespace,
	EquatableReadOnlyList<CommandRouteModel> Commands,
	EquatableReadOnlyList<CallbackRouteModel> Callbacks,
	EquatableReadOnlyList<InlineQueryRouteModel> InlineQueries,
	EquatableReadOnlyList<ChosenInlineResultRouteModel> ChosenInlineResults,
	EquatableReadOnlyList<FlowStepModel> FlowSteps,
	EquatableReadOnlyList<OnHandlerModel> OnMessages,
	EquatableReadOnlyList<OnHandlerModel> OnCallbackQueries,
	EquatableReadOnlyList<OnHandlerModel> OnInlineQueries,
	EquatableReadOnlyList<OnHandlerModel> OnChosenInlineResults);
