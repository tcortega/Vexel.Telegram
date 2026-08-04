using Telegram.Bot.Types;

namespace Vexel.Telegram.Handlers.Contexts;

/// <summary>
/// Scoped, write-once holder for the per-update context graph.
/// The dispatcher sets this once before handlers resolve; contexts and <see cref="Feedback"/>
/// read it via scoped factories.
/// </summary>
public sealed class UpdateContextHolder
{
	private const string OutsideScopeMessage =
		"Update context was resolved outside a Vexel update scope. " +
		"Contexts and Feedback are only available while an update is being dispatched.";

	private BoundContext? _bound;

	/// <summary>True after <see cref="Set"/> has been called for this scope.</summary>
	public bool IsSet => Volatile.Read(ref _bound) is not null;

	/// <summary>The inbound update bound to this scope.</summary>
	/// <exception cref="InvalidOperationException">Thrown when accessed outside a Vexel update scope.</exception>
	public Update Update => Require().Update;

	/// <summary>
	/// Message context when the update carries <see cref="Update.Message"/>; otherwise null.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown when accessed outside a Vexel update scope.</exception>
	public MessageContext? Message => Require().Message;

	/// <summary>
	/// Callback context when the update carries <see cref="Update.CallbackQuery"/>; otherwise null.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown when accessed outside a Vexel update scope.</exception>
	public CallbackContext? Callback => Require().Callback;

	/// <summary>
	/// Inline-query context when the update carries <see cref="Update.InlineQuery"/>; otherwise null.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown when accessed outside a Vexel update scope.</exception>
	public InlineQueryContext? InlineQuery => Require().InlineQuery;

	/// <summary>
	/// Chosen-inline-result context when the update carries <see cref="Update.ChosenInlineResult"/>; otherwise null.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown when accessed outside a Vexel update scope.</exception>
	public ChosenInlineResultContext? ChosenInlineResult => Require().ChosenInlineResult;

	/// <summary>
	/// Binds this scope to <paramref name="update"/>. May only be called once.
	/// </summary>
	/// <param name="update">The inbound Telegram update.</param>
	/// <exception cref="InvalidOperationException">Thrown when called a second time in the same scope.</exception>
	public void Set(Update update)
	{
		ArgumentNullException.ThrowIfNull(update);

		var bound = new BoundContext(
			update,
			update.Message is { } message ? new MessageContext(message) : null,
			update.CallbackQuery is { } callbackQuery ? new CallbackContext(callbackQuery) : null,
			update.InlineQuery is { } inlineQuery ? new InlineQueryContext(inlineQuery) : null,
			update.ChosenInlineResult is { } chosenInlineResult
				? new ChosenInlineResultContext(chosenInlineResult)
				: null);

		if (Interlocked.CompareExchange(ref _bound, bound, comparand: null) is not null)
		{
			throw new InvalidOperationException(
				"UpdateContextHolder.Set may only be called once per update scope.");
		}
	}

	private BoundContext Require() =>
		Volatile.Read(ref _bound)
		?? throw new InvalidOperationException(OutsideScopeMessage);

	private sealed record BoundContext(
		Update Update,
		MessageContext? Message,
		CallbackContext? Callback,
		InlineQueryContext? InlineQuery,
		ChosenInlineResultContext? ChosenInlineResult);
}
