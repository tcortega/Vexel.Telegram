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
		Assert.True(InlineQueryKeyExtractor.TryExtract(query, out var trigger, out var remainder));
		Assert.Equal(expectedTrigger, trigger);
		Assert.Equal(expectedRemainder, remainder);
	}
}
