using System.Globalization;
using Immediate.Handlers.Shared;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types.InlineQueryResults;
using Vexel.Telegram.Handlers;
using Vexel.Telegram.Handlers.Attributes;
using Vexel.Telegram.Handlers.Contexts;

namespace Vexel.Telegram.Sample.Handlers;

/// <summary>
/// Triggered when the first whitespace token of the query is <c>search</c>.
/// Remainder after the token binds to <see cref="Query.Text"/>.
/// </summary>
[Handler]
[InlineQuery("search")]
public static partial class SearchInline
{
	public sealed record Query(string Text);

	private static async ValueTask HandleAsync(Query query, Feedback feedback, CancellationToken token)
	{
		var term = string.IsNullOrWhiteSpace(query.Text) ? "anything" : query.Text.Trim();
		// ResultId prefix `item` matches [ChosenInlineResult("item")]; suffix after | is bound there.
		await feedback.AnswerInlineAsync(
			[
				new InlineQueryResultArticle(
					id: $"item|{term}",
					title: $"Search: {term}",
					inputMessageContent: new InputTextMessageContent($"You searched for {term}")),
				new InlineQueryResultArticle(
					id: $"item|{term}-docs",
					title: $"Docs hit for {term}",
					inputMessageContent: new InputTextMessageContent($"Docs-ish result for {term}")),
			],
			cacheTime: 0,
			isPersonal: true,
			cancellationToken: token);
	}
}

/// <summary>
/// Default inline handler: empty query, or first token that is not a registered trigger.
/// Receives the *full* query text as its string parameter.
/// </summary>
[Handler]
[InlineQuery]
public static partial class DefaultInline
{
	public sealed record Query(string Text);

	private static async ValueTask HandleAsync(Query query, Feedback feedback, CancellationToken token)
	{
		var label = string.IsNullOrEmpty(query.Text) ? "(empty query)" : query.Text;
		await feedback.AnswerInlineAsync(
			[
				new InlineQueryResultArticle(
					id: "item|default",
					title: "Default inline handler",
					inputMessageContent: new InputTextMessageContent(
						$"Default [InlineQuery] saw: {label}\n\nTry: search kittens")),
			],
			cacheTime: 0,
			isPersonal: true,
			cancellationToken: token);
	}
}

/// <summary>Fires when the user picks an inline result whose ResultId starts with <c>item</c>.</summary>
[Handler]
[ChosenInlineResult("item")]
public static partial class ItemChosen
{
	public sealed record Command(string Term);

	private static ValueTask HandleAsync(
		Command command,
		ChosenInlineResultContext context,
		ILogger<Command> logger,
		CancellationToken token)
	{
		// Chosen results are often chatless; log instead of forcing a DM.
		logger.LogInformation(
			"Chosen inline result from {User}: term={Term}, resultId={ResultId}, inlineMessageId={InlineMessageId}",
			context.From.Username ?? context.From.Id.ToString(CultureInfo.InvariantCulture),
			command.Term,
			context.ResultId,
			context.InlineMessageId ?? "(none)");
		return default;
	}
}
