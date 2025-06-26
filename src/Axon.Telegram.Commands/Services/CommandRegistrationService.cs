using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Remora.Commands.Services;
using Remora.Commands.Trees.Nodes;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Axon.Telegram.Commands.Services;

/// <summary>
/// A hosted service that registers commands with Telegram on startup by traversing the command tree.
/// </summary>
public class CommandRegistrationService(
    ILogger<CommandRegistrationService> logger,
    CommandService commandService,
    ITelegramBotClient botClient,
    IOptions<CommandOptions> options)
    : IHostedService
{
    private readonly CommandOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.Prefix != "/" || !_options.RegisterCommands)
        {
            logger.LogDebug("Skipping command registration because prefix is not '/' or registration is disabled.");
            return;
        }

        // The CommandService holds a TreeAccessor, which is the correct way to get command trees.
        // We get the default tree by passing `null` as the name.
        if (!commandService.TreeAccessor.TryGetNamedTree(null, out var tree))
        {
            logger.LogWarning("Could not find the default command tree. Commands will not be registered.");
            return;
        }

        logger.LogInformation("Traversing command tree to register commands with Telegram...");

        var discoveredCommands = new List<BotCommand>();

        // We start the recursive discovery from the root of the command tree.
        DiscoverCommands(tree.Root, new(), discoveredCommands);

        if (discoveredCommands.Count == 0)
        {
            logger.LogInformation("No commands found to register.");
            return;
        }

        await botClient.SetMyCommands(discoveredCommands, cancellationToken: cancellationToken);

        logger.LogInformation("Successfully registered {Count} commands with Telegram.", discoveredCommands.Count);
    }

    /// <summary>
    /// Recursively traverses the command tree to discover all executable commands.
    /// </summary>
    /// <param name="node">The current node in the tree to process.</param>
    /// <param name="path">The command path built so far (e.g., "settings user").</param>
    /// <param name="discoveredCommands">The list to add discovered commands to.</param>
    private static void DiscoverCommands(IParentNode node, StringBuilder path, List<BotCommand> discoveredCommands)
    {
        foreach (var childNode in node.Children)
        {
            var currentPathLength = path.Length;
            if (currentPathLength > 0)
            {
                path.Append(' ');
            }

            // The 'Key' property holds the name of the command or group.
            path.Append(childNode.Key);

            // If the node is an executable command, we add it to our list.
            if (childNode is CommandNode commandNode)
            {
                // The description is found by looking for a DescriptionAttribute in the node's Attributes list.
                var description = commandNode.Attributes
                                      .OfType<DescriptionAttribute>()
                                      .FirstOrDefault()
                                      ?.Description
                                  ?? "No description available.";

                discoveredCommands.Add(new()
                {
                    Command = path.ToString().ToLowerInvariant(),
                    Description = description
                });
            }

            // If the node is a group, we continue traversing its children.
            if (childNode is GroupNode groupNode)
            {
                DiscoverCommands(groupNode, path, discoveredCommands);
            }

            // Backtrack the path for the next sibling node.
            path.Length = currentPathLength;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}