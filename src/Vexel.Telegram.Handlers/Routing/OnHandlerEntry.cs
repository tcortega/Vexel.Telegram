namespace Vexel.Telegram.Handlers.Routing;

/// <summary>
/// One On* observer binder plus the handler type's fully-qualified metadata name.
/// Dispatch order is sorted by <see cref="HandlerFullyQualifiedName"/> (ordinal).
/// </summary>
/// <param name="HandlerFullyQualifiedName">
/// Handler type display string used as the stable sort key (e.g. <c>global::Demo.LogMessage</c>).
/// </param>
/// <param name="Binder">
/// Binder that resolves <c>X.Handler</c> from the update scope and awaits <c>HandleAsync</c>.
/// The route payload is ignored (On* requests are empty records).
/// </param>
public sealed record OnHandlerEntry(string HandlerFullyQualifiedName, RouteBinder Binder);
