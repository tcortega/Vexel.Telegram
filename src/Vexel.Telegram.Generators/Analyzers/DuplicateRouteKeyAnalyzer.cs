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
			var inlineQueries = new ConcurrentDictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
			var chosenInlineResults = new ConcurrentDictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);

			startContext.RegisterSymbolAction(
				actionContext =>
				{
					AnalyzeCommand(actionContext, commands);
					AnalyzeCallback(actionContext, callbacks);
					AnalyzeInlineQuery(actionContext, inlineQueries);
					AnalyzeChosenInlineResult(actionContext, chosenInlineResults);
				},
				SymbolKind.NamedType);

			// Diagnostics are reported at discovery time when a duplicate is found; the end action
			// only keeps the per-compilation maps alive for the duration of the analysis.
			startContext.RegisterCompilationEndAction(endContext =>
			{
				_ = commands;
				_ = callbacks;
				_ = inlineQueries;
				_ = chosenInlineResults;
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

	private static void AnalyzeInlineQuery(
		SymbolAnalysisContext context,
		ConcurrentDictionary<string, INamedTypeSymbol> inlineQueries)
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
			return;
		}

		// Empty trigger is the default handler; still unique per compilation (two defaults = VEX0005).
		var displayKey = trigger.Length == 0 ? "<default>" : trigger;
		ReportIfDuplicate(context, inlineQueries, trigger, type, attribute, kind: "inline query", displayKey);
	}

	private static void AnalyzeChosenInlineResult(
		SymbolAnalysisContext context,
		ConcurrentDictionary<string, INamedTypeSymbol> chosenInlineResults)
	{
		if (context.Symbol is not INamedTypeSymbol type)
		{
			return;
		}

		var attribute = type.GetChosenInlineResultAttribute();
		if (attribute is null)
		{
			return;
		}

		if (attribute.ConstructorArguments is not [{ Value: string key }])
		{
			return;
		}

		ReportIfDuplicate(context, chosenInlineResults, key, type, attribute, kind: "chosen inline result");
	}

	private static void ReportIfDuplicate(
		SymbolAnalysisContext context,
		ConcurrentDictionary<string, INamedTypeSymbol> map,
		string key,
		INamedTypeSymbol type,
		AttributeData attribute,
		string kind,
		string? displayKey = null)
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

		var shown = displayKey ?? key;

		// Report on both the original and the duplicate.
		context.ReportDiagnostic(
			Diagnostic.Create(
				DiagnosticDescriptors.VEX0005DuplicateRouteKey,
				location,
				kind,
				shown,
				other.ToDisplayString(),
				type.ToDisplayString()));

		foreach (var otherLocation in other.Locations)
		{
			context.ReportDiagnostic(
				Diagnostic.Create(
					DiagnosticDescriptors.VEX0005DuplicateRouteKey,
					otherLocation,
					kind,
					shown,
					type.ToDisplayString(),
					other.ToDisplayString()));
		}
	}
}
