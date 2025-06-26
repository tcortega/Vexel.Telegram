namespace Axon.Telegram.Commands.Services;

/// <summary>
/// A service responsible for holding and retrieving the command context for the current DI scope.
/// This is a direct adaptation of the service from Remora.Discord.Commands.
/// </summary>
public class ContextInjectionService
{
    /// <summary>
    /// Gets or sets the command context for the current scope.
    /// </summary>
    public ICommandContext? Context { get; set; }
}
