using Vexel.Telegram.Handlers.Routing;

namespace Vexel.Telegram.Tests.Routing;

public sealed class CallbackKeyExtractorTests
{
	[Theory]
	[InlineData("confirm", "confirm", "")]
	[InlineData("confirm|42", "confirm", "42")]
	[InlineData("pick|a|b|c", "pick", "a|b|c")]
	[InlineData("", "", "")]
	[InlineData("|only-suffix", "", "only-suffix")]
	public void Extracts_key_and_suffix(string data, string expectedKey, string expectedSuffix)
	{
		Assert.True(CallbackKeyExtractor.TryExtract(data, out var key, out var suffix));
		Assert.Equal(expectedKey, key);
		Assert.Equal(expectedSuffix, suffix);
	}

	[Fact]
	public void Null_data_is_not_extractable()
	{
		Assert.False(CallbackKeyExtractor.TryExtract(data: null, out var key, out var suffix));
		Assert.Null(key);
		Assert.Null(suffix);
	}
}
