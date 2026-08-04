using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vexel.Telegram.Generators.Analyzers;

/// <summary>
/// VEX0003: Telegram Bot API command name charset/length rules.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InvalidCommandNameAnalyzer : DiagnosticAnalyzer
{
	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.VEX0003InvalidCommandName];

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

		var attribute = type.GetCommandAttribute();
		if (attribute is null)
		{
			return;
		}

		if (attribute.ConstructorArguments is not [{ Value: string name }])
		{
			return;
		}

		if (CommandNameValidation.IsValid(name))
		{
			return;
		}

		var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
			?? type.Locations.FirstOrDefault();

		context.ReportDiagnostic(
			Diagnostic.Create(
				DiagnosticDescriptors.VEX0003InvalidCommandName,
				location,
				name));
	}
}
