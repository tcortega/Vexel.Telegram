using System.Text;
using Telegram.Bot.Types.ReplyMarkups;
using Vexel.Telegram.Handlers.Keyboards;

namespace Vexel.Telegram.Tests.Keyboards;

public sealed class InlineKeyboardBuilderTests
{
	[Fact]
	public void AddCallbackButton_Emits_short_route_key()
	{
		var markup = new InlineKeyboardBuilder()
			.AddCallbackButton("OK", "confirm")
			.Build();

		var button = Assert.IsType<InlineKeyboardButton>(Assert.Single(Assert.Single(markup.InlineKeyboard)));
		Assert.Equal("OK", button.Text);
		Assert.Equal("confirm", button.CallbackData);
	}

	[Fact]
	public void AddCallbackButton_Emits_key_pipe_suffix()
	{
		var markup = new InlineKeyboardBuilder()
			.AddCallbackButton("Edit", "item", "42")
			.Build();

		var button = Assert.IsType<InlineKeyboardButton>(Assert.Single(Assert.Single(markup.InlineKeyboard)));
		Assert.Equal("item|42", button.CallbackData);
	}

	[Fact]
	public void AddCallbackButton_Allows_suffix_with_pipes()
	{
		var markup = new InlineKeyboardBuilder()
			.AddCallbackButton("Go", "pick", "a|b|c")
			.Build();

		var button = Assert.IsType<InlineKeyboardButton>(Assert.Single(Assert.Single(markup.InlineKeyboard)));
		Assert.Equal("pick|a|b|c", button.CallbackData);
	}

	[Fact]
	public void AddCallbackButton_Rejects_pipe_in_route_key()
	{
		var builder = new InlineKeyboardBuilder();
		var ex = Assert.Throws<ArgumentException>(() => builder.AddCallbackButton("X", "bad|key"));
		Assert.Contains("|", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void AddCallbackButton_Rejects_data_over_64_utf8_bytes()
	{
		var longSuffix = new string('x', 70);
		var builder = new InlineKeyboardBuilder();
		var ex = Assert.Throws<ArgumentException>(() => builder.AddCallbackButton("X", "k", longSuffix));
		Assert.Contains("64", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void CallbackData_Format_counts_utf8_bytes_not_chars()
	{
		// Each '€' is 3 UTF-8 bytes. 22 * 3 = 66 > 64.
		var key = new string('€', 22);
		Assert.True(Encoding.UTF8.GetByteCount(key) > CallbackData.MaxUtf8ByteLength);
		_ = Assert.Throws<ArgumentException>(() => CallbackData.Format(key));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void CallbackData_Format_rejects_blank_route_key(string routeKey)
	{
		var ex = Assert.Throws<ArgumentException>(() => CallbackData.Format(routeKey));
		Assert.Contains("empty or whitespace", ex.Message, StringComparison.Ordinal);

		var builder = new InlineKeyboardBuilder();
		_ = Assert.Throws<ArgumentException>(() => builder.AddCallbackButton("X", routeKey));
	}

	[Fact]
	public void Multi_row_keyboard_preserves_structure()
	{
		var markup = new InlineKeyboardBuilder()
			.AddCallbackButton("A", "a")
			.AddCallbackButton("B", "b")
			.AddRow()
			.AddUrlButton("Docs", "https://example.com")
			.Build();

		var rows = markup.InlineKeyboard.ToArray();
		Assert.Equal(2, rows.Length);
		Assert.Equal(2, rows[0].Count());
		_ = Assert.Single(rows[1]);
	}
}
