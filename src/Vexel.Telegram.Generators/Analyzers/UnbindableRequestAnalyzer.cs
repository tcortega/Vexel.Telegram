using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vexel.Telegram.Generators.Analyzers;

/// <summary>
/// VEX0004: request shapes must follow the binding convention table.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnbindableRequestAnalyzer : DiagnosticAnalyzer
{
	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.VEX0004UnbindableRequest];

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

		// Only validate types that are dual-attr complete; VEX0001 covers missing [Handler].
		if (!type.HasHandlerAttribute())
		{
			return;
		}

		string? error = null;

		if (type.GetCommandAttribute() is not null)
		{
			_ = RouteGenerator.TryGetBindableRequest(type, out _, out _, out error);
		}

		if (error is null && type.GetCallbackAttribute() is not null)
		{
			_ = RouteGenerator.TryGetBindableStringOrEmptyRequest(type, "Callback", out _, out _, out error);
		}

		if (error is null && type.GetInlineQueryAttribute() is not null)
		{
			_ = RouteGenerator.TryGetBindableStringOrEmptyRequest(type, "Inline query", out _, out _, out error);
		}

		if (error is null && type.GetChosenInlineResultAttribute() is not null)
		{
			_ = RouteGenerator.TryGetBindableStringOrEmptyRequest(
				type,
				"Chosen inline result",
				out _,
				out _,
				out error);
		}

		if (error is null)
		{
			return;
		}

		context.ReportDiagnostic(
			Diagnostic.Create(
				DiagnosticDescriptors.VEX0004UnbindableRequest,
				type.Locations.FirstOrDefault(),
				error));
	}
}
