using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vexel.Telegram.Generators.Analyzers;

/// <summary>
/// VEX0007: <c>Flow.PromptAsync&lt;TNext&gt;</c> requires a <c>[Handler]</c> with a flow-bindable request.
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
		context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
	}

	private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
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

		var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
		var method = symbolInfo.Symbol as IMethodSymbol
			?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

		// Fall back to member-access symbol when the invocation itself did not bind cleanly
		// (e.g. missing optional-arg metadata in sparse test compilations).
		if (method is null && invocation.Expression is MemberAccessExpressionSyntax memberAccess)
		{
			var memberSymbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken);
			method = memberSymbol.Symbol as IMethodSymbol
				?? memberSymbol.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
		}

		if (method is null || !IsFlowType(method.ContainingType))
		{
			// Last resort: receiver type is Flow even if the method symbol did not bind.
			if (invocation.Expression is not MemberAccessExpressionSyntax receiverAccess)
			{
				return;
			}

			var receiverType = context.SemanticModel.GetTypeInfo(receiverAccess.Expression, context.CancellationToken).Type
				as INamedTypeSymbol;
			if (!IsFlowType(receiverType))
			{
				return;
			}
		}
		else if (!IsFlowType(method.ContainingType))
		{
			return;
		}

		var typeArgSyntax = genericName.TypeArgumentList.Arguments[0];
		if (context.SemanticModel.GetTypeInfo(typeArgSyntax, context.CancellationToken).Type is not INamedTypeSymbol targetType
			|| targetType.TypeKind == TypeKind.Error)
		{
			Report(
				context,
				typeArgSyntax,
				"Flow.PromptAsync type argument must be a named [Handler] type with a flow-bindable request.");
			return;
		}

		if (!targetType.HasHandlerAttribute())
		{
			Report(
				context,
				typeArgSyntax,
				$"Type '{targetType.ToDisplayString()}' is not marked with [Handler]; Flow.PromptAsync requires a handler type (VEX0007).");
			return;
		}

		if (RouteGenerator.TryGetBindableFlowRequest(targetType, out _, out _, out var error) && error is null)
		{
			return;
		}

		Report(
			context,
			typeArgSyntax,
			error
			?? $"Type '{targetType.ToDisplayString()}' is not a valid Flow.PromptAsync target; request must be empty or a single string (binding convention rule 5).");
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
}
