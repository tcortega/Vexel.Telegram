namespace Axon.Telegram.Client.Responders;

/// <summary>
/// Enumerates various responder groups. Responders registered within a group run in parallel, but are ordered among
/// the groups.
/// </summary>
public enum ResponderGroup
{
    /// <summary>
    /// This responder runs before all other groups.
    /// </summary>
    Early,

    /// <summary>
    /// This responder runs when responders normally run.
    /// </summary>
    Normal,

    /// <summary>
    /// This responder runs after all other groups.
    /// </summary>
    Late
}