using JetBrains.Annotations;
using Remora.Commands.Attributes;

namespace Axon.Telegram.Interactivity.Attributes;

/// <summary>
/// Marks a method in an interaction group as a handler for button interactions.
/// </summary>
[MeansImplicitUse(ImplicitUseKindFlags.Access)]
public class CallbackButtonAttribute : CommandAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CallbackButtonAttribute"/> class.
    /// </summary>
    /// <param name="name">The button's custom ID, excluding Axon's prefixed metadata.</param>
    /// <param name="aliases">The button's custom ID aliases, excluding Axon's prefixed metadata.</param>
    public CallbackButtonAttribute(string name, params string[] aliases)
        : base($"{Constants.CallbackButtonPrefix}::{name}",
            aliases.Select(a => $"{Constants.CallbackButtonPrefix}::{a}").ToArray())
    {
    }
}