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

	public static readonly DiagnosticDescriptor VEX0002CallbackDataTooLong = new(
		id: "VEX0002",
		title: "Callback data too long or invalid",
		messageFormat: "{0}",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description:
			"Telegram rejects callback_data over 64 UTF-8 bytes. Callback and chosen-inline-result route keys "
			+ "must also not contain the '|' key/suffix separator.");

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
			+ "string/int/long/bool/decimal/enum; callback/inline/flow params empty or single string; "
			+ "DI services never bind from payload.");

	public static readonly DiagnosticDescriptor VEX0005DuplicateRouteKey = new(
		id: "VEX0005",
		title: "Duplicate route key",
		messageFormat: "Duplicate {0} route key '{1}' on '{2}' and '{3}'",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description: "Route keys must be unique per kind within a compilation.");

	public static readonly DiagnosticDescriptor VEX0006InvalidInlineQueryTrigger = new(
		id: "VEX0006",
		title: "Invalid inline query trigger",
		messageFormat:
			"Inline query trigger '{0}' is invalid; a trigger must be a single token with no whitespace "
			+ "(use [InlineQuery] or [InlineQuery(\"\")] for the default handler)",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description:
			"Inline queries route on the first whitespace token of the query text, so a trigger containing "
			+ "whitespace could never match.");

	public static readonly DiagnosticDescriptor VEX0007InvalidPromptTarget = new(
		id: "VEX0007",
		title: "Invalid Flow.PromptAsync target",
		messageFormat: "{0}",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description:
			"Flow.PromptAsync<TNext> requires TNext to be a [Handler] type with a flow-bindable "
			+ "request shape (empty record or single string; binding convention rules 5-6).");
}
