using Remora.Commands.Attributes;

namespace Vexel.Telegram.Interactivity.Attributes;

/// <summary>
/// Marks a method in an interaction group as a handler for button interactions.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="CallbackButtonAttribute"/> class.
/// </remarks>
/// <param name="name">The button's custom ID, excluding Vexel's prefixed metadata.</param>
/// <param name="aliases">The button's custom ID aliases, excluding Vexel's prefixed metadata.</param>
public sealed class CallbackButtonAttribute(string name, params string[] aliases) : CommandAttribute($"{Constants.CallbackButtonPrefix}::{name}",
	[.. aliases.Select(a => $"{Constants.CallbackButtonPrefix}::{a}")]);
