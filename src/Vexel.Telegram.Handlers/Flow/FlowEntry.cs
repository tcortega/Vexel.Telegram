namespace Vexel.Telegram.Handlers;

/// <summary>
/// One armed conversation step for a (chat, user) pair.
/// </summary>
/// <param name="StepKey">
/// Durable step key - the target handler's fully-qualified type name
/// (<see cref="Type.FullName"/>), stable across builds.
/// </param>
/// <param name="DraftJson">
/// Optional JSON-serialized draft POCO. Must stay JSON-serializable so a redis
/// <see cref="IFlowStore"/> can hold the same payload.
/// </param>
/// <param name="ExpiresAt">UTC expiry; the store treats later reads as a miss and clears.</param>
public sealed record FlowEntry(string StepKey, string? DraftJson, DateTimeOffset ExpiresAt);
