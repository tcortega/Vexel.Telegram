using Vexel.Telegram.Handlers.Keyboards;
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

	[Theory]
	[InlineData("ok")]
	[InlineData("menu")]
	[InlineData("item42")]
	[InlineData("a")]
	public void Format_then_extract_roundtrips_key_only(string routeKey)
	{
		var data = CallbackData.Format(routeKey);
		Assert.True(CallbackKeyExtractor.TryExtract(data, out var key, out var suffix));
		Assert.Equal(routeKey, key);
		Assert.Equal(string.Empty, suffix);
	}

	[Theory]
	[InlineData("ok", "1")]
	[InlineData("pick", "a|b|c")]
	[InlineData("menu", "")]
	[InlineData("x", "suffix with spaces")]
	public void Format_then_extract_roundtrips_key_and_suffix(string routeKey, string suffix)
	{
		var data = CallbackData.Format(routeKey, suffix);
		Assert.True(CallbackKeyExtractor.TryExtract(data, out var key, out var extracted));
		Assert.Equal(routeKey, key);
		Assert.Equal(suffix, extracted);
	}

	[Fact]
	public void Format_rejects_blank_and_pipe_in_key()
	{
		_ = Assert.Throws<ArgumentException>(() => CallbackData.Format(" "));
		_ = Assert.Throws<ArgumentException>(() => CallbackData.Format("a|b"));
	}

	[Fact]
	public void Extract_is_stable_across_repeated_calls()
	{
		const string data = "route|payload|with|pipes";
		for (var i = 0; i < 100; i++)
		{
			Assert.True(CallbackKeyExtractor.TryExtract(data, out var key, out var suffix));
			Assert.Equal("route", key);
			Assert.Equal("payload|with|pipes", suffix);
		}
	}
}
