namespace Axon.Telegram.Commands.Services;

/// <summary>
/// Defines configuration options for command handling.
/// </summary>
public sealed record CommandOptions
{
    /// <summary>
    /// The prefix required to execute a command.
    /// Defaults to "/".
    /// </summary>
    public string Prefix { get; set; } = "/";

    /// <summary>
    /// Whether to automatically register commands with Telegram.
    /// This is only effective if the prefix is "/".
    /// Defaults to true.
    /// </summary>
    public bool RegisterCommands { get; set; } = true;
}