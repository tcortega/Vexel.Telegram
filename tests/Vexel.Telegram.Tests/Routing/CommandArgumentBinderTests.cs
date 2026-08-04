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
		Assert.True(CommandArgumentBinder.TryParseLong("9007199254740993", out var l));
		Assert.Equal(9007199254740993L, l);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("abc")]
	[InlineData("1.5")]
	[InlineData("2147483648")] // int.MaxValue + 1
	public void TryParseInt_rejects_non_integers(string text)
	{
		Assert.False(CommandArgumentBinder.TryParseInt(text, out _));
	}

	[Theory]
	[InlineData("yes")]
	[InlineData("1")]
	[InlineData("")]
	public void TryParseBool_rejects_non_bool_tokens(string text)
	{
		Assert.False(CommandArgumentBinder.TryParseBool(text, out _));
	}

	[Theory]
	[InlineData("TRUE", true)]
	[InlineData("False", false)]
	[InlineData("true", true)]
	public void TryParseBool_is_case_insensitive(string text, bool expected)
	{
		Assert.True(CommandArgumentBinder.TryParseBool(text, out var value));
		Assert.Equal(expected, value);
	}

	[Theory]
	[InlineData("abc")]
	[InlineData("")]
	[InlineData("1.2.3")]
	public void TryParseDecimal_rejects_bad_input(string text)
	{
		Assert.False(CommandArgumentBinder.TryParseDecimal(text, out _));
	}

	[Theory]
	[InlineData("Red", SampleColor.Red)]
	[InlineData("green", SampleColor.Green)]
	[InlineData("BLUE", SampleColor.Blue)]
	public void TryParseEnum_is_case_insensitive_and_defined_only(string text, SampleColor expected)
	{
		Assert.True(CommandArgumentBinder.TryParseEnum(text, out SampleColor value));
		Assert.Equal(expected, value);
	}

	[Theory]
	[InlineData("")]
	[InlineData("Purple")]
	[InlineData("99")] // numeric value outside the defined enum range
	public void TryParseEnum_rejects_undefined(string text)
	{
		Assert.False(CommandArgumentBinder.TryParseEnum(text, out SampleColor _));
	}

	[Fact]
	public void Single_trailing_string_token_is_the_whole_payload()
	{
		Assert.True(CommandArgumentBinder.TryTokenize("  hello  world  ", tokenCount: 1, trailingTakesRest: true, out var tokens));
		Assert.Equal(["hello  world"], tokens);
	}

	[Fact]
	public void Empty_payload_with_trailing_string_yields_empty_token()
	{
		Assert.True(CommandArgumentBinder.TryTokenize(string.Empty, tokenCount: 1, trailingTakesRest: true, out var tokens));
		Assert.Equal([string.Empty], tokens);
	}

	[Fact]
	public void Empty_payload_without_trailing_string_fails_for_required_token()
	{
		Assert.False(CommandArgumentBinder.TryTokenize(string.Empty, tokenCount: 1, trailingTakesRest: false, out _));
	}

	[Fact]
	public void Leftover_tokens_fail_when_trailing_does_not_take_rest()
	{
		Assert.False(CommandArgumentBinder.TryTokenize("1 2 3", tokenCount: 2, trailingTakesRest: false, out _));
	}

	[Fact]
	public void Exact_token_count_without_trailing_rest_succeeds()
	{
		Assert.True(CommandArgumentBinder.TryTokenize("1 2", tokenCount: 2, trailingTakesRest: false, out var tokens));
		Assert.Equal(["1", "2"], tokens);
	}

	[Fact]
	public void Trailing_string_may_be_empty_when_earlier_tokens_present()
	{
		Assert.True(CommandArgumentBinder.TryTokenize("42", tokenCount: 2, trailingTakesRest: true, out var tokens));
		Assert.Equal(["42", string.Empty], tokens);
	}

	[Fact]
	public void Multi_token_trailing_rest_preserves_interior_whitespace_after_first_cut()
	{
		Assert.True(CommandArgumentBinder.TryTokenize(
			"a  b   c d",
			tokenCount: 3,
			trailingTakesRest: true,
			out var tokens));

		Assert.Equal(["a", "b", "c d"], tokens);
	}

	[Fact]
	public void Tokenize_is_stable_under_repeated_calls()
	{
		// Mutation-minded: pure helper must not depend on ambient culture or mutate input.
		const string payload = "  7  hello   there ";
		for (var i = 0; i < 50; i++)
		{
			Assert.True(CommandArgumentBinder.TryTokenize(payload, tokenCount: 2, trailingTakesRest: true, out var tokens));
			Assert.Equal(["7", "hello   there"], tokens);
		}
	}

	public enum SampleColor
	{
		Red = 0,
		Green = 1,
		Blue = 2,
	}
}
