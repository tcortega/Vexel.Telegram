using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vexel.Telegram.Generators.Analyzers;

/// <summary>
/// VEX0001: Vexel route attributes require a sibling <c>[Handler]</c> (Option A / IAPI0001 parity).
/// Seeds on Vexel attrs (inverse of the generator, which seeds on <c>[Handler]</c>).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingHandlerAttributeAnalyzer : DiagnosticAnalyzer
{
	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.VEX0001MissingHandlerAttribute];

	/// <inheritdoc />
	public override void Initialize(AnalysisContext context)
	{
		ArgumentGuard.ThrowIfNull(context);

		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.NamedType);
	}

	private static void AnalyzeSymbol(SymbolAnalysisContext context)
	{
		if (context.Symbol is not INamedTypeSymbol type)
		{
			return;
		}

		if (!type.HasVexelRouteAttribute())
		{
			return;
		}

		if (type.HasHandlerAttribute())
		{
			return;
		}

		context.ReportDiagnostic(
			Diagnostic.Create(
				DiagnosticDescriptors.VEX0001MissingHandlerAttribute,
				type.Locations.FirstOrDefault(),
				type.Name));
	}
}
