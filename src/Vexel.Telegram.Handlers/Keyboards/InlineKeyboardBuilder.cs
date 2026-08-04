using Telegram.Bot.Types.ReplyMarkups;

namespace Vexel.Telegram.Handlers.Keyboards;

/// <summary>
/// Fluent builder for <see cref="InlineKeyboardMarkup"/> with short Vexel callback route ids
/// (<c>key</c> or <c>key|suffix</c>).
/// </summary>
public sealed class InlineKeyboardBuilder
{
	private readonly List<List<InlineKeyboardButton>> _rows = [];
	private List<InlineKeyboardButton>? _currentRow;

	/// <summary>
	/// Starts a new button row. Subsequent button adds append to this row until the next
	/// <see cref="AddRow"/> call.
	/// </summary>
	/// <returns>The builder instance.</returns>
	public InlineKeyboardBuilder AddRow()
	{
		_currentRow = [];
		_rows.Add(_currentRow);
		return this;
	}

	/// <summary>
	/// Adds a callback button whose <c>callback_data</c> is the Vexel route
	/// <paramref name="routeKey"/> or <c>{routeKey}|{suffix}</c>.
	/// </summary>
	/// <param name="text">Button label.</param>
	/// <param name="routeKey">
	/// Route key matching a <c>[Callback]</c> handler. Must not contain <c>|</c>.
	/// </param>
	/// <param name="suffix">
	/// Optional suffix bound to the handler's single <see cref="string"/> request parameter.
	/// </param>
	/// <returns>The builder instance.</returns>
	public InlineKeyboardBuilder AddCallbackButton(string text, string routeKey, string? suffix = null)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(routeKey);

		EnsureCurrentRow();
		var data = CallbackData.Format(routeKey, suffix);
		_currentRow!.Add(InlineKeyboardButton.WithCallbackData(text, data));
		return this;
	}

	/// <summary>
	/// Adds a URL button.
	/// </summary>
	/// <param name="text">Button label.</param>
	/// <param name="url">URL to open.</param>
	/// <returns>The builder instance.</returns>
	public InlineKeyboardBuilder AddUrlButton(string text, string url)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(url);

		EnsureCurrentRow();
		_currentRow!.Add(InlineKeyboardButton.WithUrl(text, url));
		return this;
	}

	/// <summary>
	/// Adds a button that switches the user into inline-query mode in another chat.
	/// </summary>
	/// <param name="text">Button label.</param>
	/// <param name="query">Optional query to pre-fill.</param>
	/// <returns>The builder instance.</returns>
	public InlineKeyboardBuilder AddSwitchInlineQueryButton(string text, string query = "")
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(query);

		EnsureCurrentRow();
		_currentRow!.Add(InlineKeyboardButton.WithSwitchInlineQuery(text, query));
		return this;
	}

	/// <summary>
	/// Adds a button that switches the user into inline-query mode in the current chat.
	/// </summary>
	/// <param name="text">Button label.</param>
	/// <param name="query">Optional query to pre-fill.</param>
	/// <returns>The builder instance.</returns>
	public InlineKeyboardBuilder AddSwitchInlineQueryCurrentChatButton(string text, string query = "")
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(query);

		EnsureCurrentRow();
		_currentRow!.Add(InlineKeyboardButton.WithSwitchInlineQueryCurrentChat(text, query));
		return this;
	}

	/// <summary>
	/// Builds the inline keyboard markup.
	/// </summary>
	/// <returns>The completed markup.</returns>
	public InlineKeyboardMarkup Build() => new(_rows);

	private void EnsureCurrentRow()
	{
		if (_currentRow is null)
		{
			_ = AddRow();
		}
	}
}
