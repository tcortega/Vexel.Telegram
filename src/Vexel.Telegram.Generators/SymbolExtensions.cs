using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Vexel.Telegram.Generators;

internal static class SymbolExtensions
{
	public const string HandlerAttributeMetadataName = "Immediate.Handlers.Shared.HandlerAttribute";
	public const string CommandAttributeMetadataName = "Vexel.Telegram.Handlers.Attributes.CommandAttribute";
	public const string CallbackAttributeMetadataName = "Vexel.Telegram.Handlers.Attributes.CallbackAttribute";
	public const string InlineQueryAttributeMetadataName = "Vexel.Telegram.Handlers.Attributes.InlineQueryAttribute";
	public const string ChosenInlineResultAttributeMetadataName =
		"Vexel.Telegram.Handlers.Attributes.ChosenInlineResultAttribute";
	public const string ImmediateAssemblyIdentifierMetadataName =
		"Immediate.Handlers.Shared.ImmediateAssemblyIdentifierAttribute";

	public static bool IsHandlerAttribute([NotNullWhen(true)] this ITypeSymbol? type) =>
		type is
		{
			Name: "HandlerAttribute",
			ContainingNamespace:
			{
				Name: "Shared",
				ContainingNamespace:
				{
					Name: "Handlers",
					ContainingNamespace:
					{
						Name: "Immediate",
						ContainingNamespace.IsGlobalNamespace: true,
					},
				},
			},
		};

	public static bool IsCommandAttribute([NotNullWhen(true)] this ITypeSymbol? type) =>
		type is
		{
			Name: "CommandAttribute",
			ContainingNamespace:
			{
				Name: "Attributes",
				ContainingNamespace:
				{
					Name: "Handlers",
					ContainingNamespace:
					{
						Name: "Telegram",
						ContainingNamespace:
						{
							Name: "Vexel",
							ContainingNamespace.IsGlobalNamespace: true,
						},
					},
				},
			},
		};

	public static bool IsCallbackAttribute([NotNullWhen(true)] this ITypeSymbol? type) =>
		type is
		{
			Name: "CallbackAttribute",
			ContainingNamespace:
			{
				Name: "Attributes",
				ContainingNamespace:
				{
					Name: "Handlers",
					ContainingNamespace:
					{
						Name: "Telegram",
						ContainingNamespace:
						{
							Name: "Vexel",
							ContainingNamespace.IsGlobalNamespace: true,
						},
					},
				},
			},
		};

	public static bool IsInlineQueryAttribute([NotNullWhen(true)] this ITypeSymbol? type) =>
		type is
		{
			Name: "InlineQueryAttribute",
			ContainingNamespace:
			{
				Name: "Attributes",
				ContainingNamespace:
				{
					Name: "Handlers",
					ContainingNamespace:
					{
						Name: "Telegram",
						ContainingNamespace:
						{
							Name: "Vexel",
							ContainingNamespace.IsGlobalNamespace: true,
						},
					},
				},
			},
		};

	public static bool IsChosenInlineResultAttribute([NotNullWhen(true)] this ITypeSymbol? type) =>
		type is
		{
			Name: "ChosenInlineResultAttribute",
			ContainingNamespace:
			{
				Name: "Attributes",
				ContainingNamespace:
				{
					Name: "Handlers",
					ContainingNamespace:
					{
						Name: "Telegram",
						ContainingNamespace:
						{
							Name: "Vexel",
							ContainingNamespace.IsGlobalNamespace: true,
						},
					},
				},
			},
		};

	public static bool HasHandlerAttribute(this INamedTypeSymbol type)
	{
		foreach (var attribute in type.GetAttributes())
		{
			if (attribute.AttributeClass.IsHandlerAttribute())
			{
				return true;
			}
		}

		return false;
	}

	public static AttributeData? GetCommandAttribute(this INamedTypeSymbol type)
	{
		foreach (var attribute in type.GetAttributes())
		{
			if (attribute.AttributeClass.IsCommandAttribute())
			{
				return attribute;
			}
		}

		return null;
	}

	public static AttributeData? GetCallbackAttribute(this INamedTypeSymbol type)
	{
		foreach (var attribute in type.GetAttributes())
		{
			if (attribute.AttributeClass.IsCallbackAttribute())
			{
				return attribute;
			}
		}

		return null;
	}

	public static AttributeData? GetInlineQueryAttribute(this INamedTypeSymbol type)
	{
		foreach (var attribute in type.GetAttributes())
		{
			if (attribute.AttributeClass.IsInlineQueryAttribute())
			{
				return attribute;
			}
		}

		return null;
	}

	public static AttributeData? GetChosenInlineResultAttribute(this INamedTypeSymbol type)
	{
		foreach (var attribute in type.GetAttributes())
		{
			if (attribute.AttributeClass.IsChosenInlineResultAttribute())
			{
				return attribute;
			}
		}

		return null;
	}

	public static bool IsVexelRouteAttribute([NotNullWhen(true)] this ITypeSymbol? type) =>
		type.IsCommandAttribute()
		|| type.IsCallbackAttribute()
		|| type.IsInlineQueryAttribute()
		|| type.IsChosenInlineResultAttribute();
	// On* land in T8 and extend this check.

	public static bool HasVexelRouteAttribute(this INamedTypeSymbol type)
	{
		foreach (var attribute in type.GetAttributes())
		{
			if (attribute.AttributeClass.IsVexelRouteAttribute())
			{
				return true;
			}
		}

		return false;
	}

	public static IMethodSymbol? GetHandleMethod(this INamedTypeSymbol type)
	{
		var candidates = type
			.GetMembers()
			.OfType<IMethodSymbol>()
			.Where(static m => m.Name is "Handle" or "HandleAsync")
			.Take(2)
			.ToList();

		return candidates.Count == 1 ? candidates[0] : null;
	}

	public static bool IsBindableCommandType(this ITypeSymbol type, out BindableParameterKind kind)
	{
		kind = default;

		if (type.SpecialType == SpecialType.System_String)
		{
			kind = BindableParameterKind.String;
			return true;
		}

		if (type.SpecialType == SpecialType.System_Int32)
		{
			kind = BindableParameterKind.Int;
			return true;
		}

		if (type.SpecialType == SpecialType.System_Int64)
		{
			kind = BindableParameterKind.Long;
			return true;
		}

		if (type.SpecialType == SpecialType.System_Boolean)
		{
			kind = BindableParameterKind.Bool;
			return true;
		}

		if (type.SpecialType == SpecialType.System_Decimal)
		{
			kind = BindableParameterKind.Decimal;
			return true;
		}

		if (type.TypeKind == TypeKind.Enum)
		{
			kind = BindableParameterKind.Enum;
			return true;
		}

		return false;
	}

	public static bool IsKnownDiServiceType(this ITypeSymbol type)
	{
		var display = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

		if (string.Equals(display, "global::Vexel.Telegram.Handlers.Feedback", StringComparison.Ordinal)
			|| string.Equals(display, "global::Vexel.Telegram.Handlers.Flow", StringComparison.Ordinal)
			|| string.Equals(display, "global::Vexel.Telegram.Handlers.IFlowStore", StringComparison.Ordinal)
			|| string.Equals(display, "global::Vexel.Telegram.Handlers.Contexts.MessageContext", StringComparison.Ordinal)
			|| string.Equals(display, "global::Vexel.Telegram.Handlers.Contexts.CallbackContext", StringComparison.Ordinal)
			|| string.Equals(display, "global::Vexel.Telegram.Handlers.Contexts.InlineQueryContext", StringComparison.Ordinal)
			|| string.Equals(display, "global::Vexel.Telegram.Handlers.Contexts.ChosenInlineResultContext", StringComparison.Ordinal)
			|| string.Equals(display, "global::Telegram.Bot.ITelegramBotClient", StringComparison.Ordinal))
		{
			return true;
		}

		return false;
	}

	public static string GetAssemblyIdentifier(this Compilation compilation)
	{
		foreach (var attribute in compilation.Assembly.GetAttributes())
		{
			if (string.Equals(
					attribute.AttributeClass?.ToDisplayString(),
					ImmediateAssemblyIdentifierMetadataName,
					StringComparison.Ordinal)
				&& attribute.ConstructorArguments is [{ Value: string { Length: > 0 } identifier }]
				&& IsValidIdentifier(identifier))
			{
				return identifier;
			}
		}

		return Utility.SanitizeAssemblyName(compilation.AssemblyName ?? "Assembly");
	}

	private static bool IsValidIdentifier(string identifier) =>
		identifier.Length > 0
		&& identifier[0] != '@'
		&& SyntaxFactsCompat.IsValidIdentifier(identifier);
}

/// <summary>Avoid depending on Microsoft.CodeAnalysis.CSharp just for IsValidIdentifier in analyzers.</summary>
file static class SyntaxFactsCompat
{
	public static bool IsValidIdentifier(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return false;
		}

		if (!IsIdentifierStart(name[0]))
		{
			return false;
		}

		for (var i = 1; i < name.Length; i++)
		{
			if (!IsIdentifierPart(name[i]))
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsIdentifierStart(char c) =>
		c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_' or >= '\u0080';

	private static bool IsIdentifierPart(char c) =>
		IsIdentifierStart(c) || c is >= '0' and <= '9';
}
