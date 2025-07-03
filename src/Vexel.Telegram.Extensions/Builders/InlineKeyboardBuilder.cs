using Vexel.Telegram.Interactivity;
using Telegram.Bot.Types.ReplyMarkups;

namespace Vexel.Telegram.Extensions.Builders;

/// <summary>
/// Builder for creating inline keyboards with interaction support.
/// </summary>
public class InlineKeyboardBuilder : BuilderBase<InlineKeyboardMarkup>
{
	private readonly List<List<InlineKeyboardButton>> _rows;
	private List<InlineKeyboardButton>? _currentRow;

	/// <summary>
	/// Initializes a new instance of the <see cref="InlineKeyboardBuilder"/> class.
	/// </summary>
	public InlineKeyboardBuilder()
	{
		_rows = [];
	}

	/// <summary>
	/// Adds a new row to the keyboard.
	/// </summary>
	/// <returns>The builder instance.</returns>
	public InlineKeyboardBuilder AddRow()
	{
		_currentRow = [];
		_rows.Add(_currentRow);
		return this;
	}

	/// <summary>
	/// Adds a button with a callback handler.
	/// </summary>
	/// <param name="text">The button text.</param>
	/// <param name="callbackData">The callback data (without prefix).</param>
	/// <param name="state">Optional state data to include.</param>
	/// <returns>The builder instance.</returns>
	public InlineKeyboardBuilder AddCallbackButton(string text, string callbackData, string? state = null)
	{
		if (_currentRow == null)
		{
			_ = AddRow();
		}

		var data = InteractionIdHelper.CreateCallbackButtonId(callbackData, state);
		_currentRow!.Add(InlineKeyboardButton.WithCallbackData(text, data));
		return this;
	}

	/// <summary>
	/// Adds a URL button.
	/// </summary>
	/// <param name="text">The button text.</param>
	/// <param name="url">The URL to open.</param>
	/// <returns>The builder instance.</returns>
	public InlineKeyboardBuilder AddUrlButton(string text, string url)
	{
		if (_currentRow == null)
		{
			_ = AddRow();
		}

		_currentRow!.Add(InlineKeyboardButton.WithUrl(text, url));
		return this;
	}

	/// <summary>
	/// Adds a button that switches to inline query mode.
	/// </summary>
	/// <param name="text">The button text.</param>
	/// <param name="query">The query to switch to.</param>
	/// <returns>The builder instance.</returns>
	public InlineKeyboardBuilder AddSwitchInlineQueryButton(string text, string query = "")
	{
		if (_currentRow == null)
		{
			_ = AddRow();
		}

		_currentRow!.Add(InlineKeyboardButton.WithSwitchInlineQuery(text, query));
		return this;
	}

	/// <summary>
	/// Adds a button that switches to inline query in the current chat.
	/// </summary>
	/// <param name="text">The button text.</param>
	/// <param name="query">The query to switch to.</param>
	/// <returns>The builder instance.</returns>
	public InlineKeyboardBuilder AddSwitchInlineQueryCurrentChatButton(string text, string query = "")
	{
		if (_currentRow == null)
		{
			_ = AddRow();
		}

		_currentRow!.Add(InlineKeyboardButton.WithSwitchInlineQueryCurrentChat(text, query));
		return this;
	}

	/// <summary>
	/// Builds the inline keyboard markup.
	/// </summary>
	/// <returns>The inline keyboard markup.</returns>
	public override InlineKeyboardMarkup Build()
	{
		return new(_rows);
	}
}
