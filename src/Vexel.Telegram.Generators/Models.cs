namespace Vexel.Telegram.Generators;

internal sealed record CommandRouteModel(
	string CommandName,
	string? Description,
	string HandlerFullyQualifiedName,
	string RequestFullyQualifiedName,
	string AssemblyDisplayName,
	EquatableReadOnlyList<RequestParameterModel> Parameters);

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
	EquatableReadOnlyList<CommandRouteModel> Commands);
