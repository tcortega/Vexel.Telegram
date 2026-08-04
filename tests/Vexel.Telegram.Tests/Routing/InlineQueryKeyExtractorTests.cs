using Vexel.Telegram.Handlers.Routing;

namespace Vexel.Telegram.Tests.Routing;

public sealed class InlineQueryKeyExtractorTests
{
	[Theory]
	[InlineData(null, "", "")]
	[InlineData("", "", "")]
	[InlineData("   ", "", "")]
	[InlineData("search", "search", "")]
	[InlineData("search foo", "search", "foo")]
	[InlineData("search  foo  bar", "search", "foo  bar")]
	[InlineData("  search foo", "search", "foo")]
	[InlineData("hello world", "hello", "world")]
	[InlineData("\tsearch\tfoo", "search", "foo")]
	[InlineData("SEARCH FOO", "SEARCH", "FOO")] // case-sensitive triggers live at the router map
	public void Extracts_first_token_and_trimmed_remainder(string? query, string expectedTrigger, string expectedRemainder)
	{
		var (trigger, remainder) = InlineQueryKeyExtractor.Extract(query);

		Assert.Equal(expectedTrigger, trigger);
		Assert.Equal(expectedRemainder, remainder);
	}

	[Fact]
	public void Single_token_with_trailing_whitespace_has_empty_remainder()
	{
		var (trigger, remainder) = InlineQueryKeyExtractor.Extract("search   ");
		Assert.Equal("search", trigger);
		Assert.Equal(string.Empty, remainder);
	}

	[Fact]
	public void Extract_is_idempotent()
	{
		string?[] samples = [null, "", " ", "a", "a b", "  a  b  c  ", "\nfoo\nbar"];
		foreach (var sample in samples)
		{
			var first = InlineQueryKeyExtractor.Extract(sample);
			var second = InlineQueryKeyExtractor.Extract(sample);
			Assert.Equal(first, second);
		}
	}

	[Fact]
	public void Never_uses_inline_query_id_shape_as_input()
	{
		// Document the contract: extractor only sees query text. Callers must not pass InlineQuery.Id.
		var (trigger, remainder) = InlineQueryKeyExtractor.Extract("user typed this");
		Assert.Equal("user", trigger);
		Assert.Equal("typed this", remainder);
		Assert.DoesNotContain("iq-", trigger, StringComparison.Ordinal);
	}
}
