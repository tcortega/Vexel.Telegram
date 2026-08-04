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
	public void Extracts_first_token_and_trimmed_remainder(string? query, string expectedTrigger, string expectedRemainder)
	{
		var (trigger, remainder) = InlineQueryKeyExtractor.Extract(query);

		Assert.Equal(expectedTrigger, trigger);
		Assert.Equal(expectedRemainder, remainder);
	}
}
