using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Vexel.Telegram.Generators;

/// <summary>
/// Discovers Immediate.Handlers types that also carry a Vexel route attribute and emits
/// <c>Add{Assembly}Telegram()</c> registration plus frozen command and callback binders.
/// </summary>
[Generator]
public sealed class RouteGenerator : IIncrementalGenerator
{
	/// <inheritdoc />
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var routes = context.SyntaxProvider
			.ForAttributeWithMetadataName(
				SymbolExtensions.HandlerAttributeMetadataName,
				predicate: static (node, _) => node is TypeDeclarationSyntax,
				transform: static (ctx, token) => TransformHandler(ctx, token))
			.Where(static pair => pair.Command is not null || pair.Callback is not null)
			.WithTrackingName("VexelRoutes");

		var assemblyIdentifier = context.CompilationProvider
			.Select(static (compilation, _) => compilation.GetAssemblyIdentifier())
			.WithTrackingName("VexelAssemblyIdentifier");

		var rootNamespace = context.AnalyzerConfigOptionsProvider
			.Select(static (options, _) =>
				options.GlobalOptions.TryGetValue("build_property.rootnamespace", out var ns)
					? ns
					: string.Empty)
			.WithTrackingName("VexelRootNamespace");

		var collected = routes.Collect();

		var model = collected
			.Combine(assemblyIdentifier)
			.Combine(rootNamespace)
			.Select(static (tuple, _) =>
			{
				var ((routes, assemblyIdentifier), rootNamespace) = tuple;

				var commands = routes
					.Select(static r => r.Command)
					.Where(static c => c is not null)
					.Select(static c => c!)
					.OrderBy(static r => r.CommandName, StringComparer.OrdinalIgnoreCase)
					.ThenBy(static r => r.HandlerFullyQualifiedName, StringComparer.Ordinal)
					.ToArray();

				var callbacks = routes
					.Select(static r => r.Callback)
					.Where(static c => c is not null)
					.Select(static c => c!)
					.OrderBy(static r => r.RouteKey, StringComparer.Ordinal)
					.ThenBy(static r => r.HandlerFullyQualifiedName, StringComparer.Ordinal)
					.ToArray();

				return new AssemblyRoutesModel(
					assemblyIdentifier,
					rootNamespace,
					new EquatableReadOnlyList<CommandRouteModel>(commands),
					new EquatableReadOnlyList<CallbackRouteModel>(callbacks));
			})
			.WithTrackingName("VexelRouteModel");

		context.RegisterSourceOutput(model, static (spc, routes) => Emit(spc, routes));
	}

	private static HandlerRoutes TransformHandler(
		GeneratorAttributeSyntaxContext context,
		CancellationToken token)
	{
		token.ThrowIfCancellationRequested();

		if (context.TargetSymbol is not INamedTypeSymbol type || type.ContainingType is not null)
		{
			return default;
		}

		return new HandlerRoutes(
			TransformCommand(type, token),
			TransformCallback(type, token));
	}

	private static CommandRouteModel? TransformCommand(INamedTypeSymbol type, CancellationToken token)
	{
		token.ThrowIfCancellationRequested();

		var commandAttribute = type.GetCommandAttribute();
		if (commandAttribute is null)
		{
			return null;
		}

		token.ThrowIfCancellationRequested();

		if (commandAttribute.ConstructorArguments is not [{ Value: string commandName }])
		{
			return null;
		}

		if (!CommandNameValidation.IsValid(commandName))
		{
			// VEX0003 reports the diagnostic; skip emission.
			return null;
		}

		string? description = null;
		foreach (var named in commandAttribute.NamedArguments)
		{
			if (named is { Key: "Description", Value.Value: string d })
			{
				description = d;
			}
		}

		token.ThrowIfCancellationRequested();

		if (!TryGetBindableRequest(type, out var requestType, out var parameters, out _))
		{
			// VEX0004 reports; skip emission.
			return null;
		}

		token.ThrowIfCancellationRequested();

		return new CommandRouteModel(
			CommandName: commandName,
			Description: description,
			HandlerFullyQualifiedName: type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
			RequestFullyQualifiedName: requestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
			AssemblyDisplayName: type.ContainingAssembly.Name,
			Parameters: parameters);
	}

	private static CallbackRouteModel? TransformCallback(INamedTypeSymbol type, CancellationToken token)
	{
		token.ThrowIfCancellationRequested();

		var callbackAttribute = type.GetCallbackAttribute();
		if (callbackAttribute is null)
		{
			return null;
		}

		token.ThrowIfCancellationRequested();

		if (callbackAttribute.ConstructorArguments is not [{ Value: string routeKey }])
		{
			return null;
		}

		if (!CallbackKeyValidation.IsValid(routeKey))
		{
			// VEX0002 reports; skip emission.
			return null;
		}

		token.ThrowIfCancellationRequested();

		if (!TryGetBindableCallbackRequest(type, out var requestType, out var hasStringParameter, out _))
		{
			// VEX0004 reports; skip emission.
			return null;
		}

		token.ThrowIfCancellationRequested();

		return new CallbackRouteModel(
			RouteKey: routeKey,
			HandlerFullyQualifiedName: type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
			RequestFullyQualifiedName: requestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
			AssemblyDisplayName: type.ContainingAssembly.Name,
			HasStringParameter: hasStringParameter);
	}

	internal static bool TryGetBindableRequest(
		INamedTypeSymbol handlerType,
		[System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ITypeSymbol? requestType,
		out EquatableReadOnlyList<RequestParameterModel> parameters,
		out string? errorMessage)
	{
		requestType = null;
		parameters = EquatableReadOnlyList<RequestParameterModel>.Empty;
		errorMessage = null;

		var handleMethod = handlerType.GetHandleMethod();
		if (handleMethod is null || handleMethod.Parameters.Length == 0)
		{
			errorMessage =
				$"Handler '{handlerType.Name}' must declare a single Handle/HandleAsync method whose first parameter is the request type.";
			return false;
		}

		requestType = handleMethod.Parameters[0].Type;

		if (requestType is not INamedTypeSymbol namedRequest)
		{
			errorMessage =
				$"Request type '{requestType.ToDisplayString()}' on '{handlerType.Name}' must be a named type with one public constructor.";
			return false;
		}

		if (namedRequest.IsKnownDiServiceType())
		{
			errorMessage =
				$"DI service type '{namedRequest.ToDisplayString()}' cannot be used as a request record on '{handlerType.Name}'. "
				+ "Contexts, Feedback, and ITelegramBotClient bind via HandleAsync parameters, never from the payload.";
			return false;
		}

		var ctors = namedRequest.InstanceConstructors
			.Where(static c => c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic)
			.ToArray();

		if (ctors.Length != 1)
		{
			errorMessage =
				$"Request type '{namedRequest.ToDisplayString()}' on '{handlerType.Name}' must have exactly one public constructor (binding convention rule 6).";
			return false;
		}

		var ctor = ctors[0];
		if (ctor.Parameters.Length == 0)
		{
			parameters = EquatableReadOnlyList<RequestParameterModel>.Empty;
			return true;
		}

		var models = new RequestParameterModel[ctor.Parameters.Length];
		for (var i = 0; i < ctor.Parameters.Length; i++)
		{
			var parameter = ctor.Parameters[i];
			if (parameter.Type.IsKnownDiServiceType())
			{
				errorMessage =
					$"DI service type '{parameter.Type.ToDisplayString()}' cannot appear in request record '{namedRequest.Name}' on '{handlerType.Name}' "
					+ "(binding convention rule 7).";
				return false;
			}

			if (!parameter.Type.IsBindableCommandType(out var kind))
			{
				errorMessage =
					$"Request parameter '{parameter.Name}' of type '{parameter.Type.ToDisplayString()}' on '{handlerType.Name}' is not bindable. "
					+ "Command request params must be string, int, long, bool, decimal, or enum (binding convention rule 6).";
				return false;
			}

			var isTrailingString = kind == BindableParameterKind.String && i == ctor.Parameters.Length - 1;
			// Single string always takes the whole remainder.
			if (ctor.Parameters.Length == 1 && kind == BindableParameterKind.String)
			{
				isTrailingString = true;
			}

			// Mid-list strings are single tokens, not "rest".
			if (kind == BindableParameterKind.String && i < ctor.Parameters.Length - 1)
			{
				isTrailingString = false;
			}

			models[i] = new RequestParameterModel(
				parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
				parameter.Name,
				kind,
				isTrailingString);
		}

		// TrailingTakesRest only when the last param is string. If last is non-string, all are tokens.
		parameters = new EquatableReadOnlyList<RequestParameterModel>(models);
		return true;
	}

	internal static bool TryGetBindableCallbackRequest(
		INamedTypeSymbol handlerType,
		[System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ITypeSymbol? requestType,
		out bool hasStringParameter,
		out string? errorMessage)
	{
		requestType = null;
		hasStringParameter = false;
		errorMessage = null;

		var handleMethod = handlerType.GetHandleMethod();
		if (handleMethod is null || handleMethod.Parameters.Length == 0)
		{
			errorMessage =
				$"Handler '{handlerType.Name}' must declare a single Handle/HandleAsync method whose first parameter is the request type.";
			return false;
		}

		requestType = handleMethod.Parameters[0].Type;

		if (requestType is not INamedTypeSymbol namedRequest)
		{
			errorMessage =
				$"Request type '{requestType.ToDisplayString()}' on '{handlerType.Name}' must be a named type with one public constructor.";
			return false;
		}

		if (namedRequest.IsKnownDiServiceType())
		{
			errorMessage =
				$"DI service type '{namedRequest.ToDisplayString()}' cannot be used as a request record on '{handlerType.Name}'. "
				+ "Contexts, Feedback, and ITelegramBotClient bind via HandleAsync parameters, never from the payload.";
			return false;
		}

		var ctors = namedRequest.InstanceConstructors
			.Where(static c => c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic)
			.ToArray();

		if (ctors.Length != 1)
		{
			errorMessage =
				$"Request type '{namedRequest.ToDisplayString()}' on '{handlerType.Name}' must have exactly one public constructor (binding convention rule 6).";
			return false;
		}

		var ctor = ctors[0];
		if (ctor.Parameters.Length == 0)
		{
			hasStringParameter = false;
			return true;
		}

		if (ctor.Parameters.Length == 1
			&& ctor.Parameters[0].Type.SpecialType == SpecialType.System_String
			&& !ctor.Parameters[0].Type.IsKnownDiServiceType())
		{
			hasStringParameter = true;
			return true;
		}

		errorMessage =
			$"Callback request '{namedRequest.ToDisplayString()}' on '{handlerType.Name}' must be an empty record or a single string parameter receiving the data suffix (binding convention rule 3).";
		return false;
	}

	private static void Emit(SourceProductionContext context, AssemblyRoutesModel model)
	{
		// Always emit the registration method so apps can call AddXxxTelegram() even with zero routes.
		var source = RouteRegistrationEmitter.Emit(model);
		context.AddSource("Vexel.Telegram.Routes.g.cs", source);
	}

	private readonly record struct HandlerRoutes(CommandRouteModel? Command, CallbackRouteModel? Callback);
}
