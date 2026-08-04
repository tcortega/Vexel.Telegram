using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vexel.Telegram.Generators.Analyzers;

/// <summary>
/// VEX0006: inline query triggers must be a single whitespace-free token (or empty for the
/// default handler), because routing matches the first whitespace token of the query text.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InvalidInlineQueryTriggerAnalyzer : DiagnosticAnalyzer
{
	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.VEX0006InvalidInlineQueryTrigger];

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

		var attribute = type.GetInlineQueryAttribute();
		if (attribute is null)
		{
			return;
		}

		// The optional ctor arg defaults to "" when omitted ([InlineQuery] with no args).
		if (attribute.ConstructorArguments.Length == 0)
		{
			return;
		}

		if (attribute.ConstructorArguments is not [{ Value: string trigger }])
		{
			return;
		}

		if (InlineQueryTriggerValidation.IsValid(trigger))
		{
			return;
		}

		var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
			?? type.Locations.FirstOrDefault();

		context.ReportDiagnostic(
			Diagnostic.Create(
				DiagnosticDescriptors.VEX0006InvalidInlineQueryTrigger,
				location,
				trigger));
	}
}
