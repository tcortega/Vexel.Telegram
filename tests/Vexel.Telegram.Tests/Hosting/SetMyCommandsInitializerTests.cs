using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Vexel.Telegram.Client;
using Vexel.Telegram.Hosting;
using Vexel.Telegram.Hosting.Extensions;
using Vexel.Telegram.Tests.Fakes;

namespace Vexel.Telegram.Tests.Hosting;

public sealed class SetMyCommandsInitializerTests
{
	[Fact]
	public async Task StartAsync_RegistersCommandsFromCatalog()
	{
		var bot = new RecordingTelegramBotClient();
		var catalog = new StaticCatalog(
		[
			new BotCommandDescriptor("start", "Start the bot"),
			new BotCommandDescriptor("ping", "Ping pong"),
		]);

		var initializer = new SetMyCommandsInitializer(
			bot,
			Options.Create(new VexelClientOptions()),
			[catalog],
			NullLogger<SetMyCommandsInitializer>.Instance);

		await initializer.StartAsync(CancellationToken.None);

		var request = Assert.Single(bot.OfType<SetMyCommandsRequest>());
		var commands = request.Commands.ToArray();
		Assert.Equal(2, commands.Length);
		Assert.Contains(commands, static c => c is { Command: "start", Description: "Start the bot" });
		Assert.Contains(commands, static c => c is { Command: "ping", Description: "Ping pong" });
	}

	[Fact]
	public async Task StartAsync_OptOut_SkipsSetMyCommands()
	{
		var bot = new RecordingTelegramBotClient();
		var catalog = new StaticCatalog([new BotCommandDescriptor("start", "Start the bot")]);

		var initializer = new SetMyCommandsInitializer(
			bot,
			Options.Create(new VexelClientOptions { RegisterBotCommands = false }),
			[catalog],
			NullLogger<SetMyCommandsInitializer>.Instance);

		await initializer.StartAsync(CancellationToken.None);

		Assert.Empty(bot.OfType<SetMyCommandsRequest>());
	}

	[Fact]
	public async Task StartAsync_NoCatalog_SkipsSetMyCommands()
	{
		var bot = new RecordingTelegramBotClient();

		var initializer = new SetMyCommandsInitializer(
			bot,
			Options.Create(new VexelClientOptions()),
			[],
			NullLogger<SetMyCommandsInitializer>.Instance);

		await initializer.StartAsync(CancellationToken.None);

		Assert.Empty(bot.OfType<SetMyCommandsRequest>());
	}

	[Fact]
	public async Task StartAsync_EmptyDescription_SendsNameAsDescription()
	{
		var bot = new RecordingTelegramBotClient();
		var catalog = new StaticCatalog([new BotCommandDescriptor("status", "")]);

		var initializer = new SetMyCommandsInitializer(
			bot,
			Options.Create(new VexelClientOptions()),
			[catalog],
			NullLogger<SetMyCommandsInitializer>.Instance);

		await initializer.StartAsync(CancellationToken.None);

		var request = Assert.Single(bot.OfType<SetMyCommandsRequest>());
		var command = Assert.Single(request.Commands);
		Assert.Equal("status", command.Command);
		Assert.Equal("status", command.Description);
	}

	[Fact]
	public async Task AddTelegramService_RegistersInitializerBeforeVexelService()
	{
		var services = new ServiceCollection();
		_ = services.AddLogging();
		_ = services.AddSingleton<ITelegramBotClient>(new RecordingTelegramBotClient());
		_ = services.AddTelegramService(static _ => "token");

		// Factories resolve concrete types; inspect via build (registration order == start order).
		await using var provider = services.BuildServiceProvider();
		var hostedServices = provider.GetServices<IHostedService>().ToArray();

		Assert.Equal(2, hostedServices.Length);
		Assert.IsType<SetMyCommandsInitializer>(hostedServices[0]);
		Assert.IsType<VexelService>(hostedServices[1]);
	}

	private sealed class StaticCatalog(IReadOnlyList<BotCommandDescriptor> commands) : IBotCommandCatalog
	{
		public IReadOnlyList<BotCommandDescriptor> Commands { get; } = commands;
	}
}
