using Vexel.Telegram.Handlers.Routing;

namespace Vexel.Telegram.Tests.Routing;

public sealed class CommandArgumentBinderTests
{
	[Fact]
	public void Trailing_string_takes_rest()
	{
		Assert.True(CommandArgumentBinder.TryTokenize("3 hello there", tokenCount: 2, trailingTakesRest: true, out var tokens));
		Assert.Equal(["3", "hello there"], tokens);
	}

	[Fact]
	public void Missing_non_string_token_fails()
	{
		Assert.False(CommandArgumentBinder.TryTokenize("1", tokenCount: 2, trailingTakesRest: false, out _));
	}

	[Fact]
	public void Parses_primitives()
	{
		Assert.True(CommandArgumentBinder.TryParseInt("42", out var i));
		Assert.Equal(42, i);
		Assert.True(CommandArgumentBinder.TryParseBool("true", out var b));
		Assert.True(b);
		Assert.True(CommandArgumentBinder.TryParseDecimal("1.5", out var d));
		Assert.Equal(1.5m, d);
	}
}
