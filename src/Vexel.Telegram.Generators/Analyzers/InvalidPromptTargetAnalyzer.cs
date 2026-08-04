using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vexel.Telegram.Generators.Analyzers;

/// <summary>
/// VEX0007: <c>Flow.PromptAsync&lt;TRequest&gt;</c> requires the <em>request</em> type of a
/// <c>[Handler]</c> whose request is flow-bindable.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InvalidPromptTargetAnalyzer : DiagnosticAnalyzer
{
	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.VEX0007InvalidPromptTarget];

	/// <inheritdoc />
	public override void Initialize(AnalysisContext context)
	{
		ArgumentGuard.ThrowIfNull(context);

		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static compilationStart =>
		{
			var flowSteps = new FlowStepIndex(compilationStart.Compilation);
			compilationStart.RegisterSyntaxNodeAction(
				nodeContext => AnalyzeInvocation(nodeContext, flowSteps),
				SyntaxKind.InvocationExpression);
		});
	}

	private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, FlowStepIndex flowSteps)
	{
		if (context.Node is not InvocationExpressionSyntax invocation)
		{
			return;
		}

		// Prefer generic name form: flow.PromptAsync<T>(...)
		var genericName = invocation.Expression switch
		{
			MemberAccessExpressionSyntax { Name: GenericNameSyntax g } => g,
			GenericNameSyntax g => g,
			_ => null,
		};

		if (genericName is null
			|| genericName.Identifier.ValueText is not ("PromptAsync" or "Prompt")
			|| genericName.TypeArgumentList.Arguments.Count != 1)
		{
			return;
		}

		if (!IsFlowInvocation(context, invocation))
		{
			return;
		}

		var typeArgSyntax = genericName.TypeArgumentList.Arguments[0];
		var typeArg = context.SemanticModel.GetTypeInfo(typeArgSyntax, context.CancellationToken).Type;

		// An unbound type parameter forwarded from a generic caller can only be validated at runtime.
		if (typeArg is ITypeParameterSymbol)
		{
			return;
		}

		if (typeArg is not INamedTypeSymbol targetType || targetType.TypeKind == TypeKind.Error)
		{
			Report(
				context,
				typeArgSyntax,
				"Flow.PromptAsync type argument must be the named request type of a [Handler] (for example CollectName.Command).");
			return;
		}

		if (targetType.HasHandlerAttribute())
		{
			Report(
				context,
				typeArgSyntax,
				$"Type '{targetType.ToDisplayString()}' is the handler class; Flow.PromptAsync takes its request type instead "
				+ $"(for example {targetType.Name}.Command).");
			return;
		}

		if (flowSteps.IsRegisteredStepRequest(targetType, out var error))
		{
			return;
		}

		Report(
			context,
			typeArgSyntax,
			error
			?? $"Type '{targetType.ToDisplayString()}' is not a registered flow step request; it must be the request of a "
			+ "[Handler] and be empty or take a single string (binding convention rule 5).");
	}

	private static bool IsFlowInvocation(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
	{
		var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
		var method = symbolInfo.Symbol as IMethodSymbol
			?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

		// Fall back to the member-access symbol, then to the receiver type, when the invocation itself
		// did not bind cleanly (e.g. missing optional-arg metadata in sparse test compilations).
		if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
		{
			return method is not null && IsFlowType(method.ContainingType);
		}

		if (method is null)
		{
			var memberSymbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken);
			method = memberSymbol.Symbol as IMethodSymbol
				?? memberSymbol.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
		}

		if (method is not null && IsFlowType(method.ContainingType))
		{
			return true;
		}

		var receiverType = context.SemanticModel
			.GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type as INamedTypeSymbol;
		return IsFlowType(receiverType);
	}

	// Flow lives in Vexel.Telegram.Handlers (same namespace as Feedback).
	private static bool IsFlowType(INamedTypeSymbol? type) =>
		type is
		{
			Name: "Flow",
			ContainingNamespace:
			{
				Name: "Handlers",
				ContainingNamespace:
				{
					Name: "Telegram",
					ContainingNamespace:
					{
						Name: "Vexel",
						ContainingNamespace.IsGlobalNamespace: true,
					},
				},
			},
		};

	private static void Report(SyntaxNodeAnalysisContext context, SyntaxNode node, string message)
	{
		context.ReportDiagnostic(
			Diagnostic.Create(
				DiagnosticDescriptors.VEX0007InvalidPromptTarget,
				node.GetLocation(),
				message));
	}

	/// <summary>
	/// Resolves whether a request type is registered as a flow step. The idiomatic shape nests the
	/// request inside its handler, so the containing type is checked first; only when that fails is
	/// the source assembly scanned (once) for a <c>[Handler]</c> that declares this request.
	/// <para>
	/// Requests declared in referenced assemblies are always accepted. A referenced library can
	/// contribute flow steps through its generated <c>Add{Assembly}Telegram()</c>, but its handlers'
	/// <c>HandleAsync</c> is private and metadata is imported with
	/// <see cref="MetadataImportOptions.Public"/>, so the request shape simply cannot be inspected
	/// across an assembly boundary. Reporting an error there would be an unprovable negative;
	/// <c>Flow.PromptAsync</c> validates the composed step map at runtime instead. A request declared
	/// in this compilation cannot have its handler in a referenced assembly (that assembly would have
	/// to reference this one), so restricting reports to source requests loses no real coverage.
	/// </para>
	/// </summary>
	private sealed class FlowStepIndex(Compilation compilation)
	{
		private readonly Lazy<HashSet<INamedTypeSymbol>> _declaredStepRequests = new(
			() => BuildStepRequests(compilation),
			LazyThreadSafetyMode.ExecutionAndPublication);

		public bool IsRegisteredStepRequest(INamedTypeSymbol requestType, out string? error)
		{
			error = null;

			if (!SymbolEqualityComparer.Default.Equals(requestType.ContainingAssembly, compilation.Assembly))
			{
				return true;
			}

			if (requestType.ContainingType is { } handler && handler.HasHandlerAttribute())
			{
				if (RouteGenerator.TryGetBindableFlowRequest(handler, out var request, out _, out var handlerError)
					&& handlerError is null)
				{
					if (SymbolEqualityComparer.Default.Equals(request, requestType))
					{
						return true;
					}
				}
				else
				{
					error = handlerError;
					return false;
				}
			}

			return _declaredStepRequests.Value.Contains(requestType);
		}

		private static HashSet<INamedTypeSymbol> BuildStepRequests(Compilation compilation)
		{
			var requests = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
			foreach (var type in EnumerateTypes(compilation.Assembly.GlobalNamespace))
			{
				if (!type.HasHandlerAttribute())
				{
					continue;
				}

				if (RouteGenerator.TryGetBindableFlowRequest(type, out var request, out _, out var error)
					&& error is null
					&& request is INamedTypeSymbol namedRequest)
				{
					_ = requests.Add(namedRequest);
				}
			}

			return requests;
		}

		private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceOrTypeSymbol root)
		{
			foreach (var member in root.GetMembers())
			{
				switch (member)
				{
					case INamespaceSymbol ns:
						foreach (var nested in EnumerateTypes(ns))
						{
							yield return nested;
						}

						break;

					case INamedTypeSymbol type:
						yield return type;
						foreach (var nested in EnumerateTypes(type))
						{
							yield return nested;
						}

						break;

					default:
						break;
				}
			}
		}
	}
}
