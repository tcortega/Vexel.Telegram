using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Vexel.Telegram.Generators;

/// <summary>
/// Discovers Immediate.Handlers types that also carry a Vexel route or On* attribute and emits
/// <c>Add{Assembly}Telegram()</c> registration plus frozen command, callback, inline-query,
/// chosen-inline-result, flow step binders, and ordered On* dispatch arrays.
/// Flow steps are the pure text steps: a <c>[Handler]</c> with a flow-bindable request shape (empty
/// or single string) and <em>no</em> route/On* attribute, so <c>Flow.PromptAsync&lt;TRequest&gt;</c> can
/// resolve them by the request type's <see cref="Type.FullName"/>.
/// On* observers run after the routed handler, sequential, sorted by fully-qualified metadata name.
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
			.Where(static pair =>
				pair.Command is not null
				|| pair.Callback is not null
				|| pair.InlineQuery is not null
				|| pair.ChosenInlineResult is not null
				|| pair.FlowStep is not null
				|| pair.OnMessage is not null
				|| pair.OnCallbackQuery is not null
				|| pair.OnInlineQuery is not null
				|| pair.OnChosenInlineResult is not null)
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

				var inlineQueries = routes
					.Select(static r => r.InlineQuery)
					.Where(static c => c is not null)
					.Select(static c => c!)
					.OrderBy(static r => r.Trigger, StringComparer.Ordinal)
					.ThenBy(static r => r.HandlerFullyQualifiedName, StringComparer.Ordinal)
					.ToArray();

				var chosenInlineResults = routes
					.Select(static r => r.ChosenInlineResult)
					.Where(static c => c is not null)
					.Select(static c => c!)
					.OrderBy(static r => r.RouteKey, StringComparer.Ordinal)
					.ThenBy(static r => r.HandlerFullyQualifiedName, StringComparer.Ordinal)
					.ToArray();

				var flowSteps = routes
					.Select(static r => r.FlowStep)
					.Where(static s => s is not null)
					.Select(static s => s!)
					.OrderBy(static s => s.StepKey, StringComparer.Ordinal)
					.ThenBy(static s => s.HandlerFullyQualifiedName, StringComparer.Ordinal)
					.ToArray();

				// D9/N3: On* order is sorted by fully-qualified metadata name (not encounter order).
				var onMessages = SelectOnHandlers(routes, static r => r.OnMessage);
				var onCallbackQueries = SelectOnHandlers(routes, static r => r.OnCallbackQuery);
				var onInlineQueries = SelectOnHandlers(routes, static r => r.OnInlineQuery);
				var onChosenInlineResults = SelectOnHandlers(routes, static r => r.OnChosenInlineResult);

				return new AssemblyRoutesModel(
					assemblyIdentifier,
					rootNamespace,
					new EquatableReadOnlyList<CommandRouteModel>(commands),
					new EquatableReadOnlyList<CallbackRouteModel>(callbacks),
					new EquatableReadOnlyList<InlineQueryRouteModel>(inlineQueries),
					new EquatableReadOnlyList<ChosenInlineResultRouteModel>(chosenInlineResults),
					new EquatableReadOnlyList<FlowStepModel>(flowSteps),
					new EquatableReadOnlyList<OnHandlerModel>(onMessages),
					new EquatableReadOnlyList<OnHandlerModel>(onCallbackQueries),
					new EquatableReadOnlyList<OnHandlerModel>(onInlineQueries),
					new EquatableReadOnlyList<OnHandlerModel>(onChosenInlineResults));
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
			TransformCallback(type, token),
			TransformInlineQuery(type, token),
			TransformChosenInlineResult(type, token),
			TransformFlowStep(type, token),
			TransformOnHandler(type, static t => t.GetOnMessageAttribute(), token),
			TransformOnHandler(type, static t => t.GetOnCallbackQueryAttribute(), token),
			TransformOnHandler(type, static t => t.GetOnInlineQueryAttribute(), token),
			TransformOnHandler(type, static t => t.GetOnChosenInlineResultAttribute(), token));
	}

	private static OnHandlerModel[] SelectOnHandlers(
		System.Collections.Immutable.ImmutableArray<HandlerRoutes> routes,
		Func<HandlerRoutes, OnHandlerModel?> selector) =>
		[
			..
			routes
				.Select(selector)
				.Where(static h => h is not null)
				.Select(static h => h!)
				.OrderBy(static h => h.HandlerFullyQualifiedName, StringComparer.Ordinal),
		];

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

		if (!TryGetBindableStringOrEmptyRequest(type, "Callback", out var requestType, out var hasStringParameter, out _))
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

	private static InlineQueryRouteModel? TransformInlineQuery(INamedTypeSymbol type, CancellationToken token)
	{
		token.ThrowIfCancellationRequested();

		var attribute = type.GetInlineQueryAttribute();
		if (attribute is null)
		{
			return null;
		}

		token.ThrowIfCancellationRequested();

		// Optional ctor arg defaults to "" when omitted ([InlineQuery] with no args).
		string trigger;
		if (attribute.ConstructorArguments.Length == 0)
		{
			trigger = string.Empty;
		}
		else if (attribute.ConstructorArguments is [{ Value: string t }])
		{
			trigger = t;
		}
		else
		{
			return null;
		}

		if (!InlineQueryTriggerValidation.IsValid(trigger))
		{
			// VEX0006 reports; skip emission.
			return null;
		}

		token.ThrowIfCancellationRequested();

		if (!TryGetBindableStringOrEmptyRequest(type, "Inline query", out var requestType, out var hasStringParameter, out _))
		{
			// VEX0004 reports; skip emission.
			return null;
		}

		token.ThrowIfCancellationRequested();

		return new InlineQueryRouteModel(
			Trigger: trigger,
			HandlerFullyQualifiedName: type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
			RequestFullyQualifiedName: requestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
			AssemblyDisplayName: type.ContainingAssembly.Name,
			HasStringParameter: hasStringParameter);
	}

	private static ChosenInlineResultRouteModel? TransformChosenInlineResult(
		INamedTypeSymbol type,
		CancellationToken token)
	{
		token.ThrowIfCancellationRequested();

		var attribute = type.GetChosenInlineResultAttribute();
		if (attribute is null)
		{
			return null;
		}

		token.ThrowIfCancellationRequested();

		if (attribute.ConstructorArguments is not [{ Value: string routeKey }])
		{
			return null;
		}

		// Same key rules as callback (non-blank, no '|', <= 64 UTF-8 bytes of ResultId budget).
		if (!CallbackKeyValidation.IsValid(routeKey))
		{
			// VEX0002 reports; skip emission.
			return null;
		}

		token.ThrowIfCancellationRequested();

		if (!TryGetBindableStringOrEmptyRequest(
				type,
				"Chosen inline result",
				out var requestType,
				out var hasStringParameter,
				out _))
		{
			// VEX0004 reports; skip emission.
			return null;
		}

		token.ThrowIfCancellationRequested();

		return new ChosenInlineResultRouteModel(
			RouteKey: routeKey,
			HandlerFullyQualifiedName: type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
			RequestFullyQualifiedName: requestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
			AssemblyDisplayName: type.ContainingAssembly.Name,
			HasStringParameter: hasStringParameter);
	}

	private static FlowStepModel? TransformFlowStep(INamedTypeSymbol type, CancellationToken token)
	{
		token.ThrowIfCancellationRequested();

		// Flow steps are pure text steps only: a [Handler] with a flow-bindable request (rule 5) and no
		// route/On* attribute. A [Command]/[Callback]/[On*] handler is reached through its own path,
		// so keying it by request type would collide with pure text steps for no benefit.
		if (type.HasVexelRouteAttribute()
			|| !TryGetBindableFlowRequest(type, out var requestType, out var hasStringParameter, out _))
		{
			return null;
		}

		token.ThrowIfCancellationRequested();

		// The step key is the *request* type: handler classes are static and cannot be type arguments,
		// so Flow.PromptAsync<TRequest> keys on typeof(TRequest).FullName. Match by emitting
		// typeof(...).FullName! over the request type as the map key.
		var requestFq = requestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		return new FlowStepModel(
			StepKey: requestFq,
			HandlerFullyQualifiedName: type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
			RequestFullyQualifiedName: requestFq,
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

	/// <summary>
	/// Empty record or single string parameter - shared by callback, inline query, and chosen inline result.
	/// </summary>
	internal static bool TryGetBindableStringOrEmptyRequest(
		INamedTypeSymbol handlerType,
		string kindLabel,
		[System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ITypeSymbol? requestType,
		out bool hasStringParameter,
		out string? errorMessage)
	{
		return TryGetBindableEmptyOrStringRequest(
			handlerType,
			routeKind: kindLabel,
			ruleText: "empty record or a single string parameter (binding convention rule 3/4/6)",
			out requestType,
			out hasStringParameter,
			out errorMessage);
	}

	/// <summary>
	/// Flow-armed steps bind <c>Message.Text ?? Message.Caption ?? ""</c> as empty or single string
	/// (binding convention rule 5).
	/// </summary>
	internal static bool TryGetBindableFlowRequest(
		INamedTypeSymbol handlerType,
		[System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ITypeSymbol? requestType,
		out bool hasStringParameter,
		out string? errorMessage)
	{
		return TryGetBindableEmptyOrStringRequest(
			handlerType,
			routeKind: "Flow",
			ruleText: "empty record or a single string parameter receiving Message.Text ?? Caption (binding convention rule 5)",
			out requestType,
			out hasStringParameter,
			out errorMessage);
	}

	/// <summary>
	/// On* observers take an empty request record only; payload access is via injected context.
	/// </summary>
	internal static bool TryGetBindableOnRequest(
		INamedTypeSymbol handlerType,
		[System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ITypeSymbol? requestType,
		out string? errorMessage)
	{
		if (!TryGetBindableEmptyOrStringRequest(
			handlerType,
			routeKind: "On*",
			ruleText: "empty record (On* observers inject context for payload access)",
			out requestType,
			out var hasStringParameter,
			out errorMessage))
		{
			return false;
		}

		if (hasStringParameter)
		{
			errorMessage =
				$"On* request '{requestType.ToDisplayString()}' on '{handlerType.Name}' must be an empty record "
				+ "(inject MessageContext/CallbackContext/etc. for payload access).";
			return false;
		}

		return true;
	}

	private static OnHandlerModel? TransformOnHandler(
		INamedTypeSymbol type,
		Func<INamedTypeSymbol, AttributeData?> attributeSelector,
		CancellationToken token)
	{
		token.ThrowIfCancellationRequested();

		if (attributeSelector(type) is null)
		{
			return null;
		}

		token.ThrowIfCancellationRequested();

		if (!TryGetBindableOnRequest(type, out var requestType, out _))
		{
			// VEX0004 reports; skip emission.
			return null;
		}

		token.ThrowIfCancellationRequested();

		return new OnHandlerModel(
			HandlerFullyQualifiedName: type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
			RequestFullyQualifiedName: requestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
			AssemblyDisplayName: type.ContainingAssembly.Name);
	}

	private static bool TryGetBindableEmptyOrStringRequest(
		INamedTypeSymbol handlerType,
		string routeKind,
		string ruleText,
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
			$"{routeKind} request '{namedRequest.ToDisplayString()}' on '{handlerType.Name}' must be an {ruleText}.";
		return false;
	}

	private static void Emit(SourceProductionContext context, AssemblyRoutesModel model)
	{
		// Always emit the registration method so apps can call AddXxxTelegram() even with zero routes.
		var source = RouteRegistrationEmitter.Emit(model);
		context.AddSource("Vexel.Telegram.Routes.g.cs", source);
	}

	private readonly record struct HandlerRoutes(
		CommandRouteModel? Command,
		CallbackRouteModel? Callback,
		InlineQueryRouteModel? InlineQuery,
		ChosenInlineResultRouteModel? ChosenInlineResult,
		FlowStepModel? FlowStep,
		OnHandlerModel? OnMessage,
		OnHandlerModel? OnCallbackQuery,
		OnHandlerModel? OnInlineQuery,
		OnHandlerModel? OnChosenInlineResult);
}
