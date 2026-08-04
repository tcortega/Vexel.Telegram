using Microsoft.CodeAnalysis;

namespace Vexel.Telegram.Generators;

internal static class DiagnosticDescriptors
{
	public const string Category = "Vexel.Telegram";

	public static readonly DiagnosticDescriptor VEX0001MissingHandlerAttribute = new(
		id: "VEX0001",
		title: "[Handler] must be used",
		messageFormat: "Handler '{0}' must be marked with [Handler] so Immediate.Handlers generates X.Handler",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description: "A Vexel route attribute requires Immediate.Handlers [Handler] on the same type (Option A dual-attr model).");

	public static readonly DiagnosticDescriptor VEX0003InvalidCommandName = new(
		id: "VEX0003",
		title: "Invalid Telegram command name",
		messageFormat: "Command name '{0}' is invalid; Telegram requires 1-32 characters of lowercase a-z, digits, and underscores",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description: "BotCommand names must match the Telegram Bot API charset and length rules.");

	public static readonly DiagnosticDescriptor VEX0004UnbindableRequest = new(
		id: "VEX0004",
		title: "Unbindable request shape",
		messageFormat: "{0}",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description:
			"Request records must follow the Vexel binding convention: one public ctor; command params from "
			+ "string/int/long/bool/decimal/enum; DI services never bind from payload.");

	public static readonly DiagnosticDescriptor VEX0005DuplicateRouteKey = new(
		id: "VEX0005",
		title: "Duplicate route key",
		messageFormat: "Duplicate {0} route key '{1}' on '{2}' and '{3}'",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description: "Route keys must be unique per kind within a compilation.");
}
