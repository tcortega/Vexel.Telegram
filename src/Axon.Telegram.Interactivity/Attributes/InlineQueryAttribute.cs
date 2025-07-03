using JetBrains.Annotations;
using Remora.Commands.Attributes;

namespace Axon.Telegram.Interactivity.Attributes;

/// <summary>
/// Marks a method in an interaction group as a handler for inline query interactions.
/// </summary>
[MeansImplicitUse(ImplicitUseKindFlags.Access)]
public class InlineQueryAttribute : CommandAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InlineQueryAttribute"/> class.
    /// </summary>
    /// <param name="name">The inline query's custom ID, excluding Axon's prefixed metadata.</param>
    /// <param name="aliases">The inline query's custom ID aliases, excluding Axon's prefixed metadata.</param>
    public InlineQueryAttribute(string name, params string[] aliases)
        : base($"{Constants.InlineQueryPrefix}::{name}",
            aliases.Select(a => $"{Constants.InlineQueryPrefix}::{a}").ToArray())
    {
    }
}