using Axon.Telegram.Abstractions.Responders;
using Axon.Telegram.Client.Extensions;
using Telegram.Bot.Types;

namespace Axon.Telegram.Client.Responders;

/// <summary>
/// Handles introspection of registered responders.
/// </summary>
public class ResponderService : IResponderTypeRepository
{
    private readonly Dictionary<Type, List<Type>> _registeredEarlyResponderTypes = new();
    private readonly Dictionary<Type, List<Type>> _registeredResponderTypes = new();
    private readonly Dictionary<Type, List<Type>> _registeredLateResponderTypes = new();

    /// <summary>
    /// Adds a responder to the service.
    /// </summary>
    /// <typeparam name="TResponder">The responder type.</typeparam>
    /// <param name="group">The group the responder belongs to.</param>
    internal void RegisterResponderType<TResponder>(ResponderGroup group) where TResponder : IResponder
    {
        RegisterResponderType(typeof(TResponder), group);
    }

    /// <summary>
    /// Adds a responder to the service.
    /// </summary>
    /// <param name="responderType">The responder type.</param>
    /// <param name="group">The group the responder belongs to.</param>
    internal void RegisterResponderType(Type responderType, ResponderGroup group)
    {
        if (!responderType.IsResponder())
        {
            throw new ArgumentException
            (
                $"{nameof(responderType)} must implement {nameof(IResponder)}.",
                nameof(responderType)
            );
        }

        var responderTypeInterfaces = responderType.GetInterfaces();
        var responderInterfaces = responderTypeInterfaces.Where
        (r => r.IsGenericType && r.GetGenericTypeDefinition() == typeof(IResponder<>)
        );

        foreach (var responderInterface in responderInterfaces)
        {
            var registeredTypes = group switch
            {
                ResponderGroup.Early => _registeredEarlyResponderTypes,
                ResponderGroup.Normal => _registeredResponderTypes,
                ResponderGroup.Late => _registeredLateResponderTypes,
                _ => throw new ArgumentOutOfRangeException(nameof(group), group, null)
            };

            if (!registeredTypes.TryGetValue(responderInterface, out var responderTypeList))
            {
                responderTypeList = new List<Type>();
                registeredTypes.Add(responderInterface, responderTypeList);
            }

            if (responderTypeList.Contains(responderType))
            {
                continue;
            }

            responderTypeList.Add(responderType);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<Type> GetEarlyResponderTypes<TUpdate>()
        => GetResponderTypes<TUpdate>(_registeredEarlyResponderTypes);

    /// <inheritdoc />
    public IReadOnlyList<Type> GetResponderTypes<TUpdate>()
        => GetResponderTypes<TUpdate>(_registeredResponderTypes);

    /// <inheritdoc />
    public IReadOnlyList<Type> GetLateResponderTypes<TUpdate>()
        => GetResponderTypes<TUpdate>(_registeredLateResponderTypes);

    /// <summary>
    /// Gets all responder types that are relevant for the given event.
    /// </summary>
    /// <typeparam name="TUpdate">The event type.</typeparam>
    /// <param name="responderGroup">The responder group that responders should be retrieved from.</param>
    /// <returns>A list of responder types.</returns>
    public static IReadOnlyList<Type> GetResponderTypes<TUpdate>
    (
        IReadOnlyDictionary<Type, List<Type>> responderGroup
    )
    {
        var typeKey = typeof(IResponder<TUpdate>);

        // Fetch any responders that want all events
        if (!responderGroup.TryGetValue(typeof(IResponder<Update>), out var anyResponderTypes))
        {
            anyResponderTypes = [];
        }

        // And add on the ones wanting this event type in particular
        return responderGroup.TryGetValue(typeKey, out var typedResponderTypes)
            ? anyResponderTypes.Concat(typedResponderTypes).ToList()
            : anyResponderTypes;
    }
}