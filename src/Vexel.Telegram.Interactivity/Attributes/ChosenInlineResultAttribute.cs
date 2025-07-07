using Remora.Commands.Attributes;

namespace Vexel.Telegram.Interactivity;

/// <summary>
/// Marks a method in an interaction group as a handler for chosen inline result interactions.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ChosenInlineResultAttribute"/> class.
/// </remarks>
/// <param name="name">The chosen inline result's custom ID, excluding Vexel's prefixed metadata.</param>
/// <param name="aliases">The chosen inline result's custom ID aliases, excluding Vexel's prefixed metadata.</param>
public sealed class ChosenInlineResultAttribute(string name, params string[] aliases) : CommandAttribute(
	$"{Constants.ChosenInlineResultPrefix}::{name}", [.. aliases.Select(a => $"{Constants.ChosenInlineResultPrefix}::{a}")]);
