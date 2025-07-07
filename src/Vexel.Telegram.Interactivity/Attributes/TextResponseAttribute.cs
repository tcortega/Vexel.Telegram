using Remora.Commands.Attributes;

namespace Vexel.Telegram.Interactivity;

/// <summary>
/// Marks a method in an interaction group as a handler for text message interactions.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="TextResponseAttribute"/> class.
/// </remarks>
/// <param name="name">The text message's custom ID, excluding Vexel's prefixed metadata.</param>
public sealed class TextResponseAttribute(string name) : CommandAttribute($"{Constants.TextResponsePrefix}::{name}");
