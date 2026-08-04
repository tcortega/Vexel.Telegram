namespace Vexel.Telegram.Handlers.Routing;

/// <summary>
/// SetMyCommands metadata for one <c>[Command]</c> handler.
/// </summary>
/// <param name="Name">Command name without a leading slash.</param>
/// <param name="Description">Optional description; empty when unset.</param>
public sealed record CommandRouteMetadata(string Name, string Description);
