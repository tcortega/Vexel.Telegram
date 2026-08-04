using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vexel.Telegram.Generators.Analyzers;

/// <summary>
/// VEX0002: callback route keys must fit in Telegram's 64-byte UTF-8 callback_data limit and must
/// not contain the <c>|</c> key/suffix separator.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CallbackDataTooLongAnalyzer : DiagnosticAnalyzer
{
	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.VEX0002CallbackDataTooLong];

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

		var attribute = type.GetCallbackAttribute();
		if (attribute is null)
		{
			return;
		}

		if (attribute.ConstructorArguments is not [{ Value: string key }])
		{
			return;
		}

		if (CallbackKeyValidation.IsValid(key))
		{
			return;
		}

		var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
			?? type.Locations.FirstOrDefault();

		context.ReportDiagnostic(
			Diagnostic.Create(
				DiagnosticDescriptors.VEX0002CallbackDataTooLong,
				location,
				CallbackKeyValidation.DescribeFailure(key)));
	}
}
