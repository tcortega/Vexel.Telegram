using Vexel.Telegram.Abstractions.Responders;

namespace Vexel.Telegram.Client.Responders;

/// <summary>
///  Represents options related to <see cref="IResponderDispatchService"/>.
/// </summary>
/// <param name="MaxItems">How many items can be queued for dispatch at any given time.</param>
public record ResponderDispatchOptions(uint MaxItems = 100);
