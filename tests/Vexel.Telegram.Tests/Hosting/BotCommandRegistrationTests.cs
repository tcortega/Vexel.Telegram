using Telegram.Bot.Types;
using Vexel.Telegram.Client;

namespace Vexel.Telegram.Tests.Hosting;

public sealed class BotCommandRegistrationTests
{
	[Fact]
	public void BuildPayload_MapsNameAndDescription()
	{
		var payload = BotCommandRegistration.BuildPayload(
		[
			new BotCommandDescriptor("start", "Start the bot"),
			new BotCommandDescriptor("ping", "Ping pong"),
		]);

		Assert.Equal(2, payload.Count);
		Assert.Equal("start", payload[0].Command);
		Assert.Equal("Start the bot", payload[0].Description);
		Assert.Equal("ping", payload[1].Command);
		Assert.Equal("Ping pong", payload[1].Description);
	}

	[Fact]
	public void BuildPayload_EmptyDescription_FallsBackToCommandName()
	{
		var payload = BotCommandRegistration.BuildPayload(
		[
			new BotCommandDescriptor("help", ""),
			new BotCommandDescriptor("about", "   "),
		]);

		Assert.Equal("help", payload[0].Command);
		Assert.Equal("help", payload[0].Description);
		Assert.Equal("about", payload[1].Command);
		Assert.Equal("about", payload[1].Description);
	}

	[Fact]
	public void BuildPayload_RejectsBlankName()
	{
		_ = Assert.ThrowsAny<ArgumentException>(() =>
			BotCommandRegistration.BuildPayload([new BotCommandDescriptor("  ", "x")]));
	}
}
