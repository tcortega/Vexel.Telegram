using JetBrains.Annotations;
using Remora.Commands.Attributes;

namespace Axon.Telegram.Interactivity.Attributes;

/// <summary>
/// Marks a method in an interaction group as a handler for chosen inline result interactions.
/// </summary>
[MeansImplicitUse(ImplicitUseKindFlags.Access)]
public class ChosenInlineResultAttribute : CommandAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChosenInlineResultAttribute"/> class.
    /// </summary>
    /// <param name="name">The chosen inline result's custom ID, excluding Axon's prefixed metadata.</param>
    /// <param name="aliases">The chosen inline result's custom ID aliases, excluding Axon's prefixed metadata.</param>
    public ChosenInlineResultAttribute(string name, params string[] aliases)
        : base($"{Constants.ChosenInlineResultPrefix}::{name}", aliases.Select(a => $"{Constants.ChosenInlineResultPrefix}::{a}").ToArray())
    {
    }
}