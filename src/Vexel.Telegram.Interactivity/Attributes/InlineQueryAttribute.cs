using Remora.Commands.Attributes;

namespace Vexel.Telegram.Interactivity;

/// <summary>
/// Marks a method in an interaction group as a handler for inline query interactions.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="InlineQueryAttribute"/> class.
/// </remarks>
/// <param name="name">The inline query's custom ID, excluding Vexel's prefixed metadata.</param>
/// <param name="aliases">The inline query's custom ID aliases, excluding Vexel's prefixed metadata.</param>
public sealed class InlineQueryAttribute(string name, params string[] aliases) : CommandAttribute($"{Constants.InlineQueryPrefix}::{name}",
	[.. aliases.Select(a => $"{Constants.InlineQueryPrefix}::{a}")]);
