using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vexel.Telegram.AspNetCore.Extensions;
using Vexel.Telegram.Client;
using Vexel.Telegram.Client.Dispatch;
using Vexel.Telegram.Handlers;
using Vexel.Telegram.Handlers.Contexts;
using Vexel.Telegram.Handlers.Routing;
using Vexel.Telegram.Hosting;

namespace Vexel.Telegram.Tests.Api;

/// <summary>
/// Pins what the four shipped packages expose to an application, which is the surface a consumer
/// actually compiles against: the debloated types and DI helpers must be gone or internal, and the
/// seams the packages are extended through must stay public.
/// </summary>
public sealed class PublicSurfaceTests
{
	private static readonly Assembly[] Packages =
	[
		typeof(VexelClient).Assembly,
		typeof(TelegramRouter).Assembly,
		typeof(VexelService).Assembly,
		typeof(TelegramWebhookEndpointExtensions).Assembly,
	];

	/// <summary>Types that must no longer be reachable from outside the packages.</summary>
	private static readonly string[] RemovedOrDemotedTypes =
	[
		"IUpdateDispatcher",
		"CallbackAnswerObligation",
		"InlineAnswerObligation",
		"AnswerObligation",
		"CommandRouteMetadata",
		"BotCommandRegistration",
		"RawUpdateHandlerRegistry",
		"UpdateContextScopeInitializer",
		"CommandKeyExtractor",
		"CallbackKeyExtractor",
		"InlineQueryKeyExtractor",
	];

	/// <summary>DI helpers that are now internal composition detail of <c>AddTelegramBot</c>.</summary>
	private static readonly string[] PeeledDiMethods =
	[
		"AddTelegramRouter",
		"AddTelegramFlow",
		"AddVexelUpdateContexts",
	];

	[Fact]
	public void Debloated_types_and_di_helpers_are_no_longer_public()
	{
		var exported = Packages.SelectMany(static a => a.GetExportedTypes()).ToArray();

		foreach (var name in RemovedOrDemotedTypes)
		{
			Assert.DoesNotContain(exported, t => string.Equals(t.Name, name, StringComparison.Ordinal));
		}

		var publicMethods = exported
			.SelectMany(static t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
			.Where(static m => m.IsPublic)
			.ToArray();

		foreach (var name in PeeledDiMethods)
		{
			Assert.DoesNotContain(publicMethods, m => string.Equals(m.Name, name, StringComparison.Ordinal));
		}

		// Hosting keeps the IServiceCollection entry point and drops the IHostBuilder overload.
		var hostingEntryPoints = publicMethods
			.Where(static m => string.Equals(m.Name, "AddTelegramService", StringComparison.Ordinal))
			.ToArray();

		_ = Assert.Single(hostingEntryPoints);
		Assert.Equal(typeof(IServiceCollection), hostingEntryPoints[0].GetParameters()[0].ParameterType);
		Assert.DoesNotContain(
			publicMethods,
			static m => m.GetParameters().Any(static p => p.ParameterType == typeof(IHostBuilder)));

		_ = EvidenceWriter.TryWrite("public-surface-after-debloat.md", BuildEvidence(exported));
	}

	[Fact]
	public void Package_seams_stay_public()
	{
		// Extension points an application implements or calls; each one has to be visible outside
		// the package, which `IsVisible` (unlike `typeof`, which sees InternalsVisibleTo) proves.
		Type[] seams =
		[
			typeof(IRawUpdateHandler),
			typeof(IFlowStore),
			typeof(IUpdateRouter),
			typeof(IBotCommandCatalog),
			typeof(IUpdateScopeInitializer),
			typeof(IUpdateCompletionHook),
			typeof(CommandArgumentBinder),
			typeof(UpdateDispatcher),
			typeof(Feedback),
			typeof(Flow),
			typeof(MessageContext),
			typeof(CallbackContext),
			typeof(InlineQueryContext),
			typeof(ChosenInlineResultContext),
			typeof(Telegram.Handlers.Keyboards.InlineKeyboardBuilder),
			typeof(Telegram.Handlers.Keyboards.CallbackData),
			typeof(BotCommandDescriptor),
		];

		foreach (var seam in seams)
		{
			Assert.True(seam.IsVisible, $"{seam.FullName} must stay part of the public surface.");
		}

		foreach (var (declaringType, method) in ((Type, string)[])
			[
				(typeof(Telegram.Handlers.DependencyInjection.ServiceCollectionExtensions), "AddTelegramBot"),
				(typeof(Client.Extensions.ServiceCollectionExtensions), "AddRawUpdateHandler"),
				(typeof(Client.Extensions.ServiceCollectionExtensions), "AddVexelTelegramClient"),
				(typeof(Telegram.Hosting.Extensions.ServiceCollectionExtensions), "AddTelegramService"),
				(typeof(TelegramWebhookEndpointExtensions), "MapTelegramWebhook"),
			])
		{
			Assert.Contains(
				declaringType.GetMethods(BindingFlags.Public | BindingFlags.Static),
				m => string.Equals(m.Name, method, StringComparison.Ordinal));
		}
	}

	[Fact]
	public void Consumer_code_cannot_reach_the_debloated_surface_but_still_wires_a_bot()
	{
		// Compiled as somebody else's assembly, so `InternalsVisibleTo("Vexel.Telegram.Tests")`
		// does not apply: this is what an application's own project sees.
		var log = new StringBuilder();
		log.AppendLine("# What an application's own project sees");
		log.AppendLine();
		log.AppendLine("Each line below was compiled into an assembly named `SomeoneElses.Bot`, which the");
		log.AppendLine("packages do not grant internals access to - so this is a consumer's compiler output.");
		log.AppendLine();
		log.AppendLine("## Removed or internal: consumer code no longer compiles");
		log.AppendLine();
		log.AppendLine("```text");

		foreach (var (symbol, snippet) in RemovedFromConsumerCode)
		{
			var errors = CompileConsumer(snippet);

			Assert.NotEmpty(errors);
			Assert.Contains(errors, e => e.Contains(symbol, StringComparison.Ordinal));

			log.AppendLine(CultureInfo.InvariantCulture, $"> {snippet.Trim()}");
			foreach (var error in errors)
			{
				log.AppendLine(CultureInfo.InvariantCulture, $"  {error}");
			}
		}

		log.AppendLine("```");
		log.AppendLine();

		// The supported wiring still compiles for that same consumer.
		var supported = CompileConsumer(
			"""
					_ = services.AddTelegramBot(static _ => "424242:token");
					_ = services.AddRawUpdateHandler<ConsumerRawHandler>();
			""");

		Assert.Empty(supported);

		log.AppendLine("## Supported wiring: compiles clean");
		log.AppendLine();
		log.AppendLine("```csharp");
		log.AppendLine("services.AddTelegramBot(_ => token);          // 0 errors");
		log.AppendLine("services.AddRawUpdateHandler<MyRawHandler>(); // 0 errors");
		log.AppendLine("```");
		log.AppendLine();

		_ = EvidenceWriter.TryWrite("consumer-compile-surface.md", log.ToString());
	}

	/// <summary>Consumer snippets that must fail to compile, with the symbol the error must name.</summary>
	private static readonly (string Symbol, string Snippet)[] RemovedFromConsumerCode =
	[
		("AddTelegramRouter", "\t\t_ = services.AddTelegramRouter();"),
		("AddTelegramFlow", "\t\t_ = services.AddTelegramFlow();"),
		("AddVexelUpdateContexts", "\t\t_ = services.AddVexelUpdateContexts();"),
		("IUpdateDispatcher", "\t\t_ = typeof(IUpdateDispatcher);"),
		("CallbackAnswerObligation", "\t\t_ = typeof(CallbackAnswerObligation);"),
		("InlineAnswerObligation", "\t\t_ = typeof(InlineAnswerObligation);"),
		("AnswerObligation", "\t\t_ = typeof(AnswerObligation);"),
		("CommandRouteMetadata", "\t\t_ = typeof(CommandRouteMetadata);"),
		("BotCommandRegistration", "\t\t_ = typeof(BotCommandRegistration);"),
		("RawUpdateHandlerRegistry", "\t\t_ = typeof(RawUpdateHandlerRegistry);"),
		("CommandKeyExtractor", "\t\t_ = typeof(CommandKeyExtractor);"),
		("CallbackKeyExtractor", "\t\t_ = typeof(CallbackKeyExtractor);"),
		("InlineQueryKeyExtractor", "\t\t_ = typeof(InlineQueryKeyExtractor);"),
		("AddTelegramService", "\t\tIHostBuilder builder = null!; _ = builder.AddTelegramService(static _ => \"t\");"),
	];

	/// <summary>Compiles one consumer snippet and returns the compiler errors it produced.</summary>
	private static string[] CompileConsumer(string body)
	{
		var source = $$"""
			using System;
			using System.Threading;
			using System.Threading.Tasks;
			using Microsoft.Extensions.DependencyInjection;
			using Microsoft.Extensions.Hosting;
			using Telegram.Bot.Types;
			using Vexel.Telegram.Client;
			using Vexel.Telegram.Client.Dispatch;
			using Vexel.Telegram.Client.Extensions;
			using Vexel.Telegram.Handlers;
			using Vexel.Telegram.Handlers.Contexts;
			using Vexel.Telegram.Handlers.DependencyInjection;
			using Vexel.Telegram.Handlers.Routing;
			using Vexel.Telegram.Hosting;
			using Vexel.Telegram.Hosting.Extensions;

			public static class ConsumerBot
			{
				public static void Wire(IServiceCollection services)
				{
			{{body}}
				}
			}

			public sealed class ConsumerRawHandler : IRawUpdateHandler
			{
				public Task HandleAsync(Update update, CancellationToken cancellationToken) => Task.CompletedTask;
			}
			""";

		var compilation = CSharpCompilation.Create(
			assemblyName: "SomeoneElses.Bot",
			syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
			references: ConsumerReferences(),
			options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		return
		[
			.. compilation.GetDiagnostics()
				.Where(static d => d.Severity is DiagnosticSeverity.Error)
				.Select(static d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}"),
		];
	}

	private static MetadataReference[] ConsumerReferences()
	{
		var platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
			.Split(Path.PathSeparator)
			.Select(static p => (MetadataReference)MetadataReference.CreateFromFile(p));

		return
		[
			.. platform,
			.. Packages.Select(static a => (MetadataReference)MetadataReference.CreateFromFile(a.Location)),
		];
	}

	private static string BuildEvidence(IReadOnlyList<Type> exported)
	{
		var writer = new StringBuilder();

		writer.AppendLine("# Public surface after the debloat (slices A + B)");
		writer.AppendLine();
		writer.AppendLine(
			"Every type an application can see from the shipped packages, read back off the compiled");
		writer.AppendLine("assemblies with reflection. This is what a consumer compiles against.");
		writer.AppendLine();

		foreach (var package in Packages)
		{
			writer.AppendLine(CultureInfo.InvariantCulture, $"## {package.GetName().Name}");
			writer.AppendLine();
			writer.AppendLine("```text");
			foreach (var type in exported
				.Where(t => t.Assembly == package)
				.OrderBy(static t => t.FullName, StringComparer.Ordinal))
			{
				writer.AppendLine(Describe(type));
			}

			writer.AppendLine("```");
			writer.AppendLine();
		}

		writer.AppendLine("## Gone from the public surface");
		writer.AppendLine();
		writer.AppendLine("| Symbol | Kind |");
		writer.AppendLine("| --- | --- |");
		foreach (var name in RemovedOrDemotedTypes)
		{
			writer.AppendLine(CultureInfo.InvariantCulture, $"| `{name}` | type: deleted or internal |");
		}

		foreach (var name in PeeledDiMethods)
		{
			writer.AppendLine(CultureInfo.InvariantCulture, $"| `{name}` | DI helper: internal |");
		}

		writer.AppendLine("| `IHostBuilder.AddTelegramService` | overload: deleted |");
		writer.AppendLine();

		return writer.ToString();
	}

	private static string Describe(Type type)
	{
		var kind = type switch
		{
			{ IsInterface: true } => "interface",
			{ IsEnum: true } => "enum",
			{ IsValueType: true } => "struct",
			{ IsAbstract: true, IsSealed: true } => "static class",
			_ => "class",
		};

		var members = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
			.Where(static m => !m.IsSpecialName)
			.Select(static m => m.Name)
			.Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)
			.ToArray();

		return members.Length == 0
			? $"{kind} {type.FullName}"
			: $"{kind} {type.FullName}  [{string.Join(", ", members)}]";
	}
}
