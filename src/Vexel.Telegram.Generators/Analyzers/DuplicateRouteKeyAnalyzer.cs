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
			var callbacks = new ConcurrentDictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);

			startContext.RegisterSymbolAction(
				actionContext =>
				{
					AnalyzeCommand(actionContext, commands);
					AnalyzeCallback(actionContext, callbacks);
				},
				SymbolKind.NamedType);

			// Diagnostics are reported at discovery time when a duplicate is found; the end action
			// only keeps the per-compilation maps alive for the duration of the analysis.
			startContext.RegisterCompilationEndAction(endContext =>
			{
				_ = commands;
				_ = callbacks;
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

		ReportIfDuplicate(context, commands, name, type, attribute, kind: "command");
	}

	private static void AnalyzeCallback(
		SymbolAnalysisContext context,
		ConcurrentDictionary<string, INamedTypeSymbol> callbacks)
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

		ReportIfDuplicate(context, callbacks, key, type, attribute, kind: "callback");
	}

	private static void ReportIfDuplicate(
		SymbolAnalysisContext context,
		ConcurrentDictionary<string, INamedTypeSymbol> map,
		string key,
		INamedTypeSymbol type,
		AttributeData attribute,
		string kind)
	{
		var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
			?? type.Locations.FirstOrDefault();

		if (map.TryAdd(key, type))
		{
			return;
		}

		if (!map.TryGetValue(key, out var other))
		{
			return;
		}

		// Report on both the original and the duplicate.
		context.ReportDiagnostic(
			Diagnostic.Create(
				DiagnosticDescriptors.VEX0005DuplicateRouteKey,
				location,
				kind,
				key,
				other.ToDisplayString(),
				type.ToDisplayString()));

		foreach (var otherLocation in other.Locations)
		{
			context.ReportDiagnostic(
				Diagnostic.Create(
					DiagnosticDescriptors.VEX0005DuplicateRouteKey,
					otherLocation,
					kind,
					key,
					type.ToDisplayString(),
					other.ToDisplayString()));
		}
	}
}
