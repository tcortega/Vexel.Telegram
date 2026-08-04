using System.Collections.Immutable;
using System.Reflection;
using Immediate.Handlers.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Vexel.Telegram.Generators;
using Vexel.Telegram.Generators.Analyzers;
using Vexel.Telegram.Handlers;
using Vexel.Telegram.Handlers.Attributes;
using Vexel.Telegram.Handlers.Contexts;
using Vexel.Telegram.Handlers.Routing;

namespace Vexel.Telegram.Tests.Generators;

internal static class GeneratorTestHelper
{
	public static GeneratorDriverRunResult RunGenerators(string source)
	{
		var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
		var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

		var compilation = CSharpCompilation.Create(
			assemblyName: "GeneratorTests",
			syntaxTrees: [syntaxTree],
			references: GetReferences(),
			options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		var immediateGenerator = LoadImmediateHandlersGenerator();

		GeneratorDriver driver = CSharpGeneratorDriver.Create(
			generators:
			[
				new RouteGenerator().AsSourceGenerator(),
				immediateGenerator.AsSourceGenerator(),
			],
			parseOptions: parseOptions,
			optionsProvider: new TestOptionsProvider());

		driver = driver.RunGeneratorsAndUpdateCompilation(
			compilation,
			out var outputCompilation,
			out var diagnostics);

		Assert.Empty(diagnostics.Where(static d => d.Severity is DiagnosticSeverity.Error));

		var compileDiagnostics = outputCompilation.GetDiagnostics()
			.Where(static d => d.Severity is DiagnosticSeverity.Error)
			.ToArray();

		if (compileDiagnostics.Length > 0)
		{
			var trees = string.Join(
				Environment.NewLine + "====" + Environment.NewLine,
				outputCompilation.SyntaxTrees.Select(static t => t.FilePath + Environment.NewLine + t.GetText()));
			Assert.Fail(
				string.Join(Environment.NewLine, compileDiagnostics.Select(static d => d.ToString()))
				+ Environment.NewLine
				+ trees);
		}

		return driver.GetRunResult();
	}

	public static async Task<ImmutableArray<Diagnostic>> RunAnalyzersAsync(
		string source,
		params DiagnosticAnalyzer[] analyzers)
	{
		var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
		var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

		var compilation = CSharpCompilation.Create(
			assemblyName: "AnalyzerTests",
			syntaxTrees: [syntaxTree],
			references: GetReferences(),
			options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		var withAnalyzers = compilation.WithAnalyzers([.. analyzers]);
		return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
	}

	public static string GetVexelGeneratedSource(GeneratorDriverRunResult result)
	{
		var tree = result.GeneratedTrees
			.Single(static t => t.FilePath.EndsWith("Vexel.Telegram.Routes.g.cs", StringComparison.Ordinal));
		return tree.GetText().ToString();
	}

	private static IIncrementalGenerator LoadImmediateHandlersGenerator()
	{
		var handlersSharedLocation = typeof(HandlerAttribute).Assembly.Location;
		// Prefer the NuGet package layout; fall back to scanning nearby when the assembly is
		// copied into the test output directory without the analyzers folder.
		var candidates = new List<string>();

		var dir = new DirectoryInfo(Path.GetDirectoryName(handlersSharedLocation)!);
		for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
		{
			candidates.Add(Path.Combine(
				dir.FullName,
				"analyzers", "dotnet", "roslyn4.8", "cs", "Immediate.Handlers.Generators.dll"));
			candidates.Add(Path.Combine(
				dir.FullName,
				"analyzers", "dotnet", "roslyn5.0", "cs", "Immediate.Handlers.Generators.dll"));
		}

		var nugetRoot = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			".nuget", "packages", "immediate.handlers");
		if (Directory.Exists(nugetRoot))
		{
			foreach (var dll in Directory.EnumerateFiles(
					nugetRoot,
					"Immediate.Handlers.Generators.dll",
					SearchOption.AllDirectories))
			{
				candidates.Add(dll);
			}
		}

		var generatorPath = candidates.FirstOrDefault(File.Exists);
		Assert.True(
			generatorPath is not null,
			"Immediate.Handlers generator not found. Looked in: " + string.Join(", ", candidates));

		var assembly = Assembly.LoadFrom(generatorPath);
		var type = assembly.GetTypes()
			.Single(static t => string.Equals(t.Name, "ImmediateHandlersGenerator", StringComparison.Ordinal));
		return (IIncrementalGenerator)Activator.CreateInstance(type)!;
	}

	private static ImmutableArray<MetadataReference> GetReferences()
	{
		var tpa = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
		var refs = tpa
			.Split(Path.PathSeparator)
			.Select(static p => MetadataReference.CreateFromFile(p))
			.Cast<MetadataReference>()
			.ToList();

		refs.Add(MetadataReference.CreateFromFile(typeof(HandlerAttribute).Assembly.Location));
		refs.Add(MetadataReference.CreateFromFile(typeof(CommandAttribute).Assembly.Location));
		refs.Add(MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location));
		refs.Add(MetadataReference.CreateFromFile(typeof(ServiceCollection).Assembly.Location));
		refs.Add(MetadataReference.CreateFromFile(typeof(ITelegramBotClient).Assembly.Location));
		refs.Add(MetadataReference.CreateFromFile(typeof(Feedback).Assembly.Location));
		refs.Add(MetadataReference.CreateFromFile(typeof(MessageContext).Assembly.Location));
		refs.Add(MetadataReference.CreateFromFile(typeof(RouteBinder).Assembly.Location));
		refs.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

		return [.. refs];
	}

	private sealed class TestOptionsProvider : AnalyzerConfigOptionsProvider
	{
		private static readonly AnalyzerConfigOptions s_options = new DictionaryOptions(
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["build_property.rootnamespace"] = "GeneratorTests",
			});

		public override AnalyzerConfigOptions GlobalOptions => s_options;

		public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => s_options;

		public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => s_options;
	}

	private sealed class DictionaryOptions(Dictionary<string, string> values) : AnalyzerConfigOptions
	{
		public override bool TryGetValue(string key, out string value) =>
			values.TryGetValue(key, out value!);
	}
}
