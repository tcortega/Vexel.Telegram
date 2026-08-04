using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vexel.Telegram.Generators.Analyzers;

/// <summary>
/// VEX0005: duplicate route keys per kind within a compilation.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateRouteKeyAnalyzer : DiagnosticAnalyzer
{
	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.VEX0005DuplicateRouteKey];

	/// <inheritdoc />
	public override void Initialize(AnalysisContext context)
	{
		ArgumentGuard.ThrowIfNull(context);

		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static startContext =>
		{
			var commands = new ConcurrentDictionary<string, INamedTypeSymbol>(StringComparer.OrdinalIgnoreCase);

			startContext.RegisterSymbolAction(
				actionContext => AnalyzeCommand(actionContext, commands),
				SymbolKind.NamedType);

			startContext.RegisterCompilationEndAction(endContext =>
			{
				// Diagnostics are reported at discovery time when a duplicate is found.
				_ = commands;
			});
		});
	}

	private static void AnalyzeCommand(
		SymbolAnalysisContext context,
		ConcurrentDictionary<string, INamedTypeSymbol> commands)
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

		if (attribute.ConstructorArguments is not [{ Value: string name }] || name.Length == 0)
		{
			return;
		}

		var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
			?? type.Locations.FirstOrDefault();

		if (commands.TryAdd(name, type))
		{
			return;
		}

		if (!commands.TryGetValue(name, out var other))
		{
			return;
		}

		// Report on both the original and the duplicate.
		context.ReportDiagnostic(
			Diagnostic.Create(
				DiagnosticDescriptors.VEX0005DuplicateRouteKey,
				location,
				"command",
				name,
				other.ToDisplayString(),
				type.ToDisplayString()));

		foreach (var otherLocation in other.Locations)
		{
			context.ReportDiagnostic(
				Diagnostic.Create(
					DiagnosticDescriptors.VEX0005DuplicateRouteKey,
					otherLocation,
					"command",
					name,
					type.ToDisplayString(),
					other.ToDisplayString()));
		}
	}
}
